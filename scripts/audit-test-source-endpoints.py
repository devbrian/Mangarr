#!/usr/bin/env python3
# scripts/audit-test-source-endpoints.py
"""
gh #187 — test-source V5 endpoint drift gate.

Sibling lint to `audit-inventory-endpoints.py`. That script audits INVENTORY.md
rows against the live V5 controller surface. THIS script audits the inverse
direction: it scans every `/api/v5/<resource>` literal that appears anywhere
under `src/NzbDrone.*Test*/` (in code OR comments OR XML docs) and reports any
literal whose `<resource>` segment does not exist on the live V5 controller
surface.

The motivating drift class (gh #187): Phase 15 Plan 15-10 renamed
`/api/v5/notification*` → `/api/v5/connection*`. Test sources that referenced
the old path in comments (or in latent never-called code like
`TestKit.SeedNotificationAsync`) silently went stale. `audit-inventory-endpoints.py`
did not catch this because it walks INVENTORY.md row-by-row, not test sources.

Exit codes:
  Default (informational):  always 0. Findings are printed but do not fail CI.
                            This lets the lint roll out incrementally without
                            blocking on the deep-cleanup pass tracked under
                            the follow-up issue chain.
  --enforce:                exit 1 if any stale references are found. Flip to
                            this mode once the broader drift backlog (e.g.
                            /api/v5/history and /api/v5/manualimport refs
                            surfaced on the inaugural run) is fully resolved.

Scope:
  - Greps `src/NzbDrone.*Test*/**/*.cs` for `/api/v5/<word>` literals
    (case-insensitive on the resource segment; ASP.NET routing is
    case-insensitive). Captures the surrounding line for context.
  - Builds the canonical set of V5 resource prefixes by parsing
    `Mangarr.Api.V5/**/*Controller.cs` — same `[V5ApiController(...)]` + auto-
    derive + `base(..., "<resource>", ...)` patterns as
    `audit-inventory-endpoints.py`. Single source of truth.
  - Reports a stale literal when its `<resource>` segment is not in the
    canonical set.

What this lint does NOT catch:
  - Stale METHODS within a known resource (e.g. `/api/v5/connection/oldaction`
    when the controller route is gone). Captured by
    `audit-inventory-endpoints.py` for INVENTORY-row drift, but not here.
  - Live REST requests built via string concatenation, reflection, or
    `BuildRequest("connection?skipTesting=true")` where the literal does NOT
    start with `/api/v5/`. Those should also be migrated when a resource
    renames; this lint warns by reporting the resource-prefix shape only.
"""

import argparse
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).parent.parent.resolve()
V5_DIR = REPO_ROOT / "src" / "Mangarr.Api.V5"
TEST_GLOBS = [
    "src/NzbDrone.Automation.Test",
    "src/NzbDrone.Integration.Test",
    "src/NzbDrone.Test.Common",
    "src/NzbDrone.Api.Test",
    "src/NzbDrone.Core.Test",
    "src/NzbDrone.Common.Test",
    "src/NzbDrone.Host.Test",
    "src/NzbDrone.Mono.Test",
    "src/NzbDrone.Windows.Test",
]

V5_CTRL_LITERAL = re.compile(r'\[V5ApiController\("([^"]+)"\)\]')
V5_CTRL_AUTO = re.compile(r"\[V5ApiController\]")
PROVIDER_BASE_CTOR = re.compile(r":\s*base\([^)]*?\"([a-z][a-z0-9/_-]*)\"[^)]*?\)")
CLASS_DECL = re.compile(r"public\s+(?:abstract\s+)?class\s+(\w+)Controller\b")

# Lint-suppression pragmas. Two scopes:
#
#   Line-level — same line as the finding:
#     // audit-allow: <resource> — <justification or GH#>
#   Suppresses ONLY the named <resource> on that single line. Other
#   resources on the same line stay flagged.
#
#   File-level — anywhere in the file (typically near the top, paired
#   with a justification comment):
#     // audit-allow-file: <resource> — <justification or GH#>
#   Suppresses the named <resource> across every line in that file.
#   Multiple file-level pragmas can stack (one per resource).
#
# These are the explicit-justification paths for endpoints intentionally
# deferred to a future milestone (e.g. `manualimport` is scheduled for
# v1.1 — gh #175 / gh #188). Do NOT use either pragma to silence a finding
# without a written justification; they exist to make intentional
# deferrals visible in code review, not to hide drift.
#
# Pragma matching is anchored to C# comment syntax (`//` / `///`) — a string
# literal that happens to contain the pragma text (e.g. an assertion message
# `"audit-allow: foo"`) must NOT silently suppress findings, or the
# --enforce gate would have a false-negative path (Codex P2 finding on PR #190).
# Both regexes require:
#   1. `//` (one or more leading slashes — covers `//` line and `///` doc)
#   2. only whitespace between the slashes and the literal pragma keyword
# That tight shape rules out the bulk of false-positive cases. As a further
# guard, `scan_test_sources` strips C# string literals before applying these
# regexes so a `// audit-allow: x` substring inside a quoted string can't
# trigger suppression.
AUDIT_ALLOW_PRAGMA = re.compile(
    r"//+\s*audit-allow:\s*([a-z][a-z0-9_-]*)",
    re.IGNORECASE,
)
AUDIT_ALLOW_FILE_PRAGMA = re.compile(
    r"//+\s*audit-allow-file:\s*([a-z][a-z0-9_-]*)",
    re.IGNORECASE,
)

# Strip C# string literals (regular `"..."`, interpolated `$"..."`, verbatim
# `@"..."`, and interpolated-verbatim `$@"..."`) from a snippet of C# source.
# Used by `scan_test_sources` to neutralize pragma-look-alikes inside quoted
# strings BEFORE pragma detection runs. The replacement preserves the surrounding
# line/column structure by emitting empty quoted markers (`""`) — that way no
# `/api/v5/<resource>` literals inside strings are accidentally removed (they
# stay on their original line/column for the finding-scan that follows on the
# UNSTRIPPED text).
_CSHARP_STRING_RE = re.compile(
    # Verbatim strings first (longest match wins): @"..." or $@"..." with ""
    # as the only escape. Allowed to span newlines (re.DOTALL not needed —
    # the negated char class already accepts any char except `"`).
    r'\$?@"(?:[^"]|"")*"'
    r"|"
    # Regular / interpolated strings: "..." or $"..." with `\.` escapes;
    # disallowed to span newlines (mirrors the C# compiler — a string literal
    # cannot embed a raw newline outside a verbatim form).
    r'\$?"(?:\\.|[^"\\\n])*"',
)


def strip_csharp_strings(src: str) -> str:
    return _CSHARP_STRING_RE.sub('""', src)


def strip_csharp_comments(src: str) -> str:
    """Strip // line + /* */ block comments from C# source, preserving `://` URL
    fragments (mirrors audit-inventory-endpoints.strip_csharp_comments).

    Without this, controller source with a commented example like
    `// [V5ApiController("queue/details")]` ahead of the real attribute would
    poison the canonical-resource set with the commented literal (Codex P1
    finding on PR #189: MangaQueueDetailsController.cs:25 carries exactly that
    shape, which suppressed stale `/api/v5/queue*` test references).
    """
    src = re.sub(r"(?<![:/])//[^\n]*", "", src)
    src = re.sub(r"/\*[\s\S]*?\*/", "", src)
    return src

# Match `/api/v5/<resource>` where <resource> is alphanum + dashes + underscores
# (the first path segment after /api/v5/). Stops at the next `/`, whitespace,
# quote, backtick, angle-bracket, paren, or end-of-string. Captures the
# resource segment for canonical-set lookup.
TEST_V5_REF = re.compile(
    r"/api/v5/([A-Za-z][A-Za-z0-9_-]*)",
)


def derive_resource(class_name: str, src: str) -> str | None:
    """Same precedence as audit-inventory-endpoints.derive_resource."""
    m = V5_CTRL_LITERAL.search(src)
    if m:
        return m.group(1)
    if V5_CTRL_AUTO.search(src):
        pm = PROVIDER_BASE_CTOR.search(src)
        if pm:
            return pm.group(1)
        return class_name.lower()
    return None


def collect_canonical_resources() -> set[str]:
    """Walk V5 controllers and collect the set of FIRST-segment resource names.

    `/api/v5/<resource>` is the contract; routes like
    `/api/v5/manga/lookup` contribute `manga`. Subpaths handled by
    `audit-inventory-endpoints.py` for INVENTORY-row drift.
    """
    resources: set[str] = set()
    for cs in V5_DIR.rglob("*Controller.cs"):
        try:
            raw = cs.read_text(encoding="utf-8")
        except OSError:
            continue
        # Strip C# comments BEFORE matching V5ApiController / base() / class
        # attrs — commented examples (e.g. doc-block illustrations) must not
        # poison the canonical-resource set. See strip_csharp_comments() and
        # the Codex P1 finding on PR #189.
        src = strip_csharp_comments(raw)
        cn = CLASS_DECL.search(src)
        if not cn:
            continue
        res = derive_resource(cn.group(1), src)
        if res is None:
            continue
        # The literal may itself contain a `/` (e.g. `manga/lookup`); the
        # first segment is what the lint compares against.
        resources.add(res.split("/")[0].lower())
    return resources


def scan_test_sources(canonical: set[str]) -> list[tuple[Path, int, str, str]]:
    """Scan every .cs file under TEST_GLOBS for /api/v5/<resource> literals
    whose <resource> is not in the canonical set.

    Returns list of (path, line_no, resource_segment, full_line) tuples.
    """
    findings: list[tuple[Path, int, str, str]] = []
    for relroot in TEST_GLOBS:
        root = REPO_ROOT / relroot
        if not root.exists():
            continue
        for cs in root.rglob("*.cs"):
            try:
                lines = cs.read_text(encoding="utf-8").splitlines()
            except OSError:
                continue
            file_text = "\n".join(lines)
            # Strip string literals before pragma detection so an
            # `"// audit-allow: x"` substring inside a quoted string can't
            # silently suppress findings (Codex P2 finding on PR #190).
            # IMPORTANT: pragma detection uses the stripped text, but finding
            # detection MUST use the original lines — a stale /api/v5/<resource>
            # ref inside a string literal is still a finding.
            file_text_for_pragma = strip_csharp_strings(file_text)
            file_allowed = {
                m.group(1).lower()
                for m in AUDIT_ALLOW_FILE_PRAGMA.finditer(file_text_for_pragma)
            }
            stripped_lines = file_text_for_pragma.split("\n")
            for lineno, line in enumerate(lines, start=1):
                pragma_line = (
                    stripped_lines[lineno - 1]
                    if lineno - 1 < len(stripped_lines)
                    else line
                )
                allow_match = AUDIT_ALLOW_PRAGMA.search(pragma_line)
                allowed_res = allow_match.group(1).lower() if allow_match else None
                for m in TEST_V5_REF.finditer(line):
                    res = m.group(1).lower()
                    if res in canonical:
                        continue
                    if res == allowed_res:
                        continue
                    if res in file_allowed:
                        continue
                    findings.append((cs, lineno, res, line.rstrip()))
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Audit test-source /api/v5/<resource> literals against the live "
            "V5 controller surface. Informational by default; pass --enforce "
            "to exit 1 on any finding (CI-gate mode)."
        )
    )
    parser.add_argument(
        "--enforce",
        action="store_true",
        help=(
            "Exit 1 if any stale /api/v5/<resource> references are found. "
            "Default is exit 0 (informational reporting only)."
        ),
    )
    args = parser.parse_args()

    if not V5_DIR.exists():
        print(f"ERROR: V5 controller dir not found at {V5_DIR}", file=sys.stderr)
        return 2

    canonical = collect_canonical_resources()
    if not canonical:
        print(
            "ERROR: no canonical V5 resources discovered — V5 controller "
            "directory empty or parse failed.",
            file=sys.stderr,
        )
        return 2

    findings = scan_test_sources(canonical)

    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

    mode_label = "ENFORCE (gate)" if args.enforce else "informational"
    print("=== audit-test-source-endpoints.py ===")
    print(f"Mode:                     {mode_label}")
    print(f"Canonical V5 resources:   {len(canonical):>4}  ({', '.join(sorted(canonical))})")
    print(f"Test-source files scanned under {len(TEST_GLOBS)} roots")
    print(f"Stale /api/v5/<resource> references: {len(findings):>4}")
    print()

    if findings:
        header = (
            "--- Stale /api/v5/<resource> references (GATE FAILURE) ---"
            if args.enforce
            else "--- Stale /api/v5/<resource> references (informational) ---"
        )
        print(header)
        print("    Each line below references an /api/v5/<resource> whose")
        print("    <resource> first-segment is not a live V5 controller.")
        print("    Either the controller was renamed (update the comment / live")
        print("    code) or a typo slipped past review.")
        print()
        for path, lineno, res, line in findings:
            rel = path.relative_to(REPO_ROOT).as_posix()
            print(f"  {rel}:{lineno}  [resource='{res}']")
            print(f"    {line.strip()}")
        print()
        return 1 if args.enforce else 0

    return 0


if __name__ == "__main__":
    sys.exit(main())
