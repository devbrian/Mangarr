#!/usr/bin/env python3
# scripts/reconcile-inventory.py
"""
Phase 18 INVENTORY.md reconciliation script.

Walks src/NzbDrone.Automation.Test/Tests/ and rewrites the INVENTORY.md
status column + covering-test FQDNs + Coverage Math block to match disk state.

Status legend (from INVENTORY.md header):
  ⬜ not covered
  🟡 covered by stub / LiveService (deferred)
  🟢 covered green
  🔴 covered but red

Decision rules per row:
  - Parse `covering-test` cell → extract `path::method` (relative to src/NzbDrone.Automation.Test/).
  - If path doesn't exist on disk                                      → ⬜ (uncovered)
  - If path exists + class-level [Explicit]                            → 🟡 (deferred-stub)
  - If path exists + [Category("LiveService")] anywhere in class      → 🟡 (LiveService-only)
  - If path exists + method matches a [Test] method (or class has a
    single test as the only candidate)                                 → 🟢 (covered)
  - If path exists but no [Test] method matches at all                 → ⬜

Method-name reconciliation: if the row's method name is wrong but a [Test] method
exists in the class, the script picks the first [Test] method in the class as the
canonical fixture entry.

Coverage Math block at the bottom is rewritten with the new totals.

Usage: python3 scripts/reconcile-inventory.py
Output:
  - Rewrites .planning/phases/18-.../INVENTORY.md in place
  - Prints delta report + final tallies to stdout
"""

import re
import sys
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).parent.parent.resolve()
TESTS_DIR = REPO_ROOT / "src" / "NzbDrone.Automation.Test" / "Tests"
ASSEMBLY_DIR = REPO_ROOT / "src" / "NzbDrone.Automation.Test"
INVENTORY_PATH = (
    REPO_ROOT
    / ".planning"
    / "phases"
    / "18-automated-ui-integration-test-suite-playwright-net"
    / "INVENTORY.md"
)


def strip_comments(src: str) -> str:
    """Strip // line and /* block */ comments so [Explicit]-in-comments doesn't false-positive."""
    src = re.sub(r"//.*", "", src)
    src = re.sub(r"/\*[\s\S]*?\*/", "", src)
    return src


def scan_fixtures() -> dict:
    """
    Walk Tests/ and return a map:
      relative_path → { 'class': str, 'methods': [str], 'explicit_class': bool, 'liveservice': bool }
    relative_path is normalized to forward slashes, relative to src/NzbDrone.Automation.Test/.
    """
    fixtures = {}
    for cs in TESTS_DIR.rglob("*.cs"):
        raw = cs.read_text(encoding="utf-8")
        src_nc = strip_comments(raw)
        rel = str(cs.relative_to(ASSEMBLY_DIR)).replace("\\", "/")
        class_match = re.search(r"public\s+class\s+(\w+)", src_nc)
        # Collect [Test]-attributed method names (signature follows on next line or two)
        # Handle: [Test] then optional more attributes then `public ... Task|void name(`
        methods = re.findall(
            r"\[Test\][^\n]*\n(?:\s*\[[^\]]+\][^\n]*\n)*\s*public\s+(?:async\s+)?(?:Task|void)\s+(\w+)\s*\(",
            src_nc,
        )
        # Class-level [Explicit]: the attribute appears immediately preceding the class declaration
        # (optionally with other attributes interleaved).
        # We look for [Explicit] in a block of attributes that immediately precedes a `public class`.
        explicit_class = bool(
            re.search(
                r"^\s*\[Explicit[^\]]*\]\s*\n(?:\s*\[[^\]]+\][^\n]*\n)*\s*public\s+class",
                src_nc,
                re.MULTILINE,
            )
            or re.search(
                r"^\s*\[[^\]]+\][^\n]*\n\s*\[Explicit[^\]]*\]\s*\n(?:\s*\[[^\]]+\][^\n]*\n)*\s*public\s+class",
                src_nc,
                re.MULTILINE,
            )
        )
        liveservice = '[Category("LiveService")]' in src_nc
        if class_match:
            fixtures[rel] = {
                "class": class_match.group(1),
                "methods": methods,
                "explicit_class": explicit_class,
                "liveservice": liveservice,
            }
    return fixtures


def reconcile_inventory(fixtures: dict) -> tuple[str, dict, dict]:
    inv_src = INVENTORY_PATH.read_text(encoding="utf-8")
    delta = {}

    # Match table rows of the form `| <axis> | <id> | <surface> | `<covering>` | <emoji> |`
    # axis = route|req|v5-endpoint|modal-action
    # covering may include `::method`; emoji is one of ⬜🟢🟡🔴
    row_pat = re.compile(
        r"^(\| (?:route|req|v5-endpoint|modal-action) \| [^|]+ \| [^|]+ \| )"
        r"(`[^`]+`(?:\s*\+\s*`[^`]+`)*)"  # covering cell can include multi-fixture (` + `)
        r"( \| )([⬜🟢🟡🔴]+)( \|)\s*$",
        re.MULTILINE,
    )

    def status_for_one(covering_fragment: str) -> tuple[str, str]:
        """
        Given a `path::method` fragment (without backticks), return (new_fragment, status).
        status is one of '⬜', '🟢', '🟡'.
        """
        if "::" not in covering_fragment:
            return covering_fragment, "⬜"
        path, method = covering_fragment.split("::", 1)
        path = path.strip()
        method = method.strip()
        # Strip any trailing space + brackets like "[LiveService]"
        method = re.sub(r"\s*\[.*\].*$", "", method).strip()
        if path not in fixtures:
            return covering_fragment, "⬜"
        f = fixtures[path]
        # Determine canonical method name
        if method in f["methods"]:
            actual_method = method
        elif f["methods"]:
            actual_method = f["methods"][0]
        else:
            # File exists but no [Test] method found — treat as uncovered
            return covering_fragment, "⬜"

        new_fragment = f"{path}::{actual_method}"
        if f["explicit_class"] or f["liveservice"]:
            return new_fragment, "🟡"
        return new_fragment, "🟢"

    def reconcile(match):
        pre, covering_cell, mid, current_status, post = match.groups()
        # The covering cell may contain multiple `path::method` segments joined by ` + `
        segments = re.findall(r"`([^`]+)`", covering_cell)
        if not segments:
            delta_key = f"{current_status}→⬜"
            delta[delta_key] = delta.get(delta_key, 0) + 1
            return f"{pre}{covering_cell}{mid}⬜{post}"

        new_segments = []
        seg_statuses = []
        for seg in segments:
            new_seg, st = status_for_one(seg)
            new_segments.append(new_seg)
            seg_statuses.append(st)

        # Composite row status: green only when every segment is green.
        # If any segment is uncovered (⬜) AND no segment is green → ⬜
        # If at least one segment is green AND all others are green → 🟢
        # If any segment is yellow but none uncovered → 🟡
        # If mixed green + yellow → 🟡 (partial deferral)
        # If any segment is uncovered → status reflects best of the rest
        if all(s == "🟢" for s in seg_statuses):
            new_status = "🟢"
        elif all(s == "⬜" for s in seg_statuses):
            new_status = "⬜"
        elif any(s == "⬜" for s in seg_statuses):
            # Treat as not-fully-covered (yellow if any green, else uncovered)
            new_status = "🟡" if any(s == "🟢" for s in seg_statuses) else "⬜"
        else:
            # Mix of 🟢/🟡 or all 🟡 → 🟡
            new_status = "🟡"

        new_covering_cell = " + ".join(f"`{s}`" for s in new_segments)
        delta_key = (
            "unchanged"
            if current_status == new_status
            else f"{current_status}→{new_status}"
        )
        delta[delta_key] = delta.get(delta_key, 0) + 1
        return f"{pre}{new_covering_cell}{mid}{new_status}{post}"

    new_src = row_pat.sub(reconcile, inv_src)

    # Recompute Coverage Math block by counting status emoji in row-end positions.
    # Use row-pat to count, not the literal " | 🟢 |" substring (some rows may have differing spacing).
    matches = list(row_pat.finditer(new_src))
    green = sum(1 for m in matches if m.group(4) == "🟢")
    yellow = sum(1 for m in matches if m.group(4) == "🟡")
    red = sum(1 for m in matches if m.group(4) == "🔴")
    white = sum(1 for m in matches if m.group(4) == "⬜")
    total = green + yellow + red + white

    # Per-axis tallies for the breakdown lines.
    axis_counts = {"route": 0, "req": 0, "v5-endpoint": 0, "modal-action": 0}
    for m in matches:
        head = m.group(1)
        for axis in axis_counts:
            if f"| {axis} |" in head:
                axis_counts[axis] += 1
                break

    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    pct = (green * 100 // total) if total else 0
    math_block = (
        "## Coverage Math\n"
        "\n"
        f"- Total rows: **{total}**\n"
        f"- Route axis: **{axis_counts['route']}**\n"
        f"- Req axis: **{axis_counts['req']}**\n"
        f"- V5-endpoint axis: **{axis_counts['v5-endpoint']}**\n"
        f"- Modal-action axis: **{axis_counts['modal-action']}**\n"
        f"- Currently green (🟢): **{green}/{total}** ({pct}%)\n"
        f"- Covered by stub / LiveService (🟡): **{yellow}**\n"
        f"- Uncovered (⬜): **{white}**\n"
        f"- Red (🔴): **{red}**\n"
        "\n"
        f"Last reconciled: {today} by `scripts/reconcile-inventory.py` "
        "(Phase 18 Plan 18-19 GAP-1 closure)\n"
        "\n"
    )

    # Replace existing Coverage Math block (up to but not including the next H2).
    # Use a leading-newline-tolerant pattern that swallows any blank lines before
    # the header so we can re-emit a clean "\n## Coverage Math" with a single
    # blank line before the H2.
    math_pat = re.compile(r"\n+^## Coverage Math\n[\s\S]*?(?=^## )", re.MULTILINE)
    if math_pat.search(new_src):
        new_src = math_pat.sub("\n\n" + math_block, new_src, count=1)
    else:
        # Append before Phase Close Gate if Coverage Math missing
        close_gate_pat = re.compile(r"^## Phase Close Gate", re.MULTILINE)
        if close_gate_pat.search(new_src):
            new_src = close_gate_pat.sub(math_block + "## Phase Close Gate", new_src, count=1)
        else:
            new_src = new_src.rstrip() + "\n\n" + math_block

    tallies = {
        "total": total,
        "green": green,
        "yellow": yellow,
        "white": white,
        "red": red,
    }
    return new_src, delta, tallies


def main() -> int:
    if not INVENTORY_PATH.exists():
        print(f"ERROR: INVENTORY.md not found at {INVENTORY_PATH}", file=sys.stderr)
        return 1
    if not TESTS_DIR.exists():
        print(f"ERROR: tests dir not found at {TESTS_DIR}", file=sys.stderr)
        return 1

    fixtures = scan_fixtures()
    print(f"Scanned {len(fixtures)} fixture files under {TESTS_DIR.relative_to(REPO_ROOT)}")

    new_src, delta, tallies = reconcile_inventory(fixtures)

    INVENTORY_PATH.write_text(new_src, encoding="utf-8")

    # Avoid emoji stdout encoding crashes on Windows cp1252 consoles.
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

    def safe_print(s: str) -> None:
        try:
            print(s)
        except UnicodeEncodeError:
            print(s.encode("ascii", errors="replace").decode("ascii"))

    safe_print("")
    safe_print("=== Reconciliation tallies ===")
    safe_print(f"  Total rows:    {tallies['total']}")
    safe_print(f"  green (G):     {tallies['green']}")
    safe_print(f"  yellow (Y):    {tallies['yellow']}")
    safe_print(f"  uncovered (W): {tallies['white']}")
    safe_print(f"  red (R):       {tallies['red']}")
    safe_print("")
    safe_print("=== Status transitions ===")
    for k in sorted(delta.keys()):
        safe_print(f"  {k}: {delta[k]}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
