#!/usr/bin/env python3
# scripts/audit-inventory-endpoints.py
"""
Phase 20 D-12 inventory endpoint drift gate.

Greps INVENTORY.md's `v5-endpoint` axis rows against `Mangarr.Api.V5/**/*Controller.cs`
[HttpGet/Post/Put/Delete("...")] attributes + [V5ApiController(...)] base resource names
+ [RestPostById]/[RestPutById]/[RestDeleteById] + `base(..., "<resource>", ...)` ctor.

Outputs a drift report: rows missing-from-INVENTORY (actual route not catalogued)
and rows drifted-in-INVENTORY (catalogued route no longer exists).

Pairs with reconcile-inventory.py + audit-ui-inventory.sh + audit-test-assertions.sh
as a recurring sanity gate (v2+ phases re-run on each post-merge cycle).

Exit codes:
  0 = no drift
  1 = drift detected (one or more rows missing/drifted)
"""
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).parent.parent.resolve()
INVENTORY = (
    REPO_ROOT
    / ".planning"
    / "phases"
    / "18-automated-ui-integration-test-suite-playwright-net"
    / "INVENTORY.md"
)
V5_DIR = REPO_ROOT / "src" / "Mangarr.Api.V5"


def strip_csharp_comments(src: str) -> str:
    """Strip // line comments + /* */ block comments from C# source, preserving the
    `://` URL-fragment edge case (per reconcile-inventory.py BL-04).
    """
    # Match // only when NOT preceded by `:` or `/` (spares http:// / https://, file:///).
    src = re.sub(r"(?<![:/])//[^\n]*", "", src)
    src = re.sub(r"/\*[\s\S]*?\*/", "", src)
    return src


# INVENTORY v5-endpoint row pattern. Examples:
#   | v5-endpoint | GET /api/v5/manga/lookup | AddManga search results | `Tests/AddManga/MangaLookupFixture.cs::lookup_returns_results` | ⬜ |
V5_ROW = re.compile(
    r"^\| v5-endpoint \| (\w+) ([^|]+?) \| ([^|]+?) \| (`[^|]+`) \| ([⬜🟢🟡🔴]+) \|",
    re.MULTILINE,
)

# Controller attribute patterns.
#   [V5ApiController]                  -> auto-derive from class name minus "Controller"
#   [V5ApiController("manga/lookup")]  -> literal resource override
V5_CTRL_LITERAL = re.compile(r'\[V5ApiController\("([^"]+)"\)\]')
V5_CTRL_AUTO = re.compile(r"\[V5ApiController\]")

# HTTP attributes — optionally taking a sub-path. We also collect the action method name
# (the public method declared on the next non-attribute line). The action name is needed
# only for the legacy by-id RestPostById/RestPutById/RestDeleteById dispatch — handled
# separately below.
HTTP_ATTR = re.compile(
    r"\[Http(Get|Post|Put|Delete)(?:\(\"([^\"]*)\"\))?\]",
    re.MULTILINE,
)

# REST-by-id attributes; ProviderControllerBase + many REST controllers use these instead
# of an explicit [HttpPost]/[HttpPut]/[HttpDelete]. The Rest*ById attributes inject conventional
# routes:
#   RestPostById   -> POST /             (no sub-path; same base route)
#   RestPutById    -> PUT  /{id}
#   RestDeleteById -> DELETE /{id}
# This mapping is verified against ProviderControllerBase.cs lines 78-92 (RestPostById)
# + 95-125 (RestPutById) + 180-186 (RestDeleteById).
REST_BY_ID = re.compile(r"\[(RestPostById|RestPutById|RestDeleteById)\]")
REST_BY_ID_MAP = {
    "RestPostById": ("POST", ""),
    "RestPutById": ("PUT", "{id}"),
    "RestDeleteById": ("DELETE", "{id}"),
}

# ProviderControllerBase ctor composition. Example from IndexerController.cs:19:
#   : base(signalRBroadcaster, indexerFactory, "indexer", ResourceMapper, BulkResourceMapper)
# Capture the FIRST string literal in the `base(...)` call — that's the resource name.
PROVIDER_BASE_CTOR = re.compile(r":\s*base\([^)]*?\"([a-z][a-z0-9/_-]*)\"[^)]*?\)")

# Class declaration. `public abstract class` is used by ProviderControllerBase itself; we
# only want concrete subclasses, but we still match abstract here and skip them later via
# the V5ApiController-attribute presence check.
CLASS_DECL = re.compile(r"public\s+(?:abstract\s+)?class\s+(\w+)Controller\b")

# Whether this class extends ProviderControllerBase (transitive route inheritance).
PROVIDER_BASE_PARENT = re.compile(
    r"class\s+\w+Controller\s*:\s*ProviderControllerBase<"
)

# ProviderControllerBase contributes a known set of inherited routes. Captured once
# from ProviderControllerBase.cs (lines 60-273) so the script does not re-parse the
# base on every subclass.
# Each entry is (verb, sub_path) where sub_path is appended to `/api/v5/<resource>`.
PROVIDER_BASE_ROUTES = [
    ("GET", ""),               # [HttpGet] GetAll                                  L60
    ("POST", ""),              # [RestPostById] CreateProvider                     L78
    ("PUT", "{id}"),           # [RestPutById] UpdateProvider                      L95
    ("PUT", "bulk"),           # [HttpPut("bulk")] UpdateProvider (bulk)           L127
    ("DELETE", "{id}"),        # [RestDeleteById] DeleteProvider                   L180
    ("DELETE", "bulk"),        # [HttpDelete("bulk")] DeleteProviders              L188
    ("GET", "schema"),         # [HttpGet("schema")] GetTemplates                  L197
    ("POST", "test"),          # [HttpPost("test")] Test                           L221
    ("POST", "testall"),       # [HttpPost("testall")] TestAll                     L233
    ("POST", "action/{name}"), # [HttpPost("action/{name}")] RequestAction         L260
]


def derive_resource(class_name: str, src: str) -> str | None:
    """Derive the resource path for a controller class.

    Priority:
      1. `[V5ApiController("literal")]` -> return literal verbatim.
      2. `[V5ApiController]` (no literal) -> derive from class name minus "Controller",
         lower-cased (ASP.NET routing is case-insensitive; INVENTORY uses lowercase).
      3. ProviderControllerBase descendant -> resolve from base(..., "<resource>", ...) ctor.

    Returns None when the class does not carry [V5ApiController] (it is not a V5 controller).
    """
    m = V5_CTRL_LITERAL.search(src)
    if m:
        return m.group(1)
    if V5_CTRL_AUTO.search(src):
        # Auto-derive. For ProviderControllerBase subclasses, prefer the ctor literal.
        pm = PROVIDER_BASE_CTOR.search(src)
        if pm:
            return pm.group(1)
        return class_name.lower()
    return None


def extract_controller_routes(cs_path: Path):
    """Yield (verb, full_path) tuples for one controller .cs file."""
    raw = cs_path.read_text(encoding="utf-8")
    src = strip_csharp_comments(raw)

    cn = CLASS_DECL.search(src)
    if not cn:
        return
    class_name = cn.group(1)

    resource = derive_resource(class_name, src)
    if resource is None:
        return  # Not a V5ApiController-annotated class.

    base = f"/api/v5/{resource}".rstrip("/")

    # 1. Explicit [HttpGet/Post/Put/Delete("...")] or bare [Http*].
    for m in HTTP_ATTR.finditer(src):
        verb = m.group(1).upper()
        sub = m.group(2) or ""
        path = f"{base}/{sub}".rstrip("/") if sub else base
        yield (verb, path)

    # 2. [RestPostById] / [RestPutById] / [RestDeleteById] on the controller itself.
    for m in REST_BY_ID.finditer(src):
        attr = m.group(1)
        verb, sub = REST_BY_ID_MAP[attr]
        path = f"{base}/{sub}".rstrip("/") if sub else base
        yield (verb, path)

    # 3. ProviderControllerBase inherited routes — only if the class extends it.
    if PROVIDER_BASE_PARENT.search(src):
        for verb, sub in PROVIDER_BASE_ROUTES:
            path = f"{base}/{sub}".rstrip("/") if sub else base
            yield (verb, path)


def extract_inventory_v5_rows(inv_text: str):
    """Yield (verb, path, surface, fixture_cell, status) for each v5-endpoint row.

    The fixture cell is returned verbatim (including backticks) so the caller can keep
    multi-fixture rows joined via ` + ` intact when reporting drift.
    """
    for m in V5_ROW.finditer(inv_text):
        verb, path, surface, fixture_cell, status = m.groups()
        yield (verb.upper(), path.strip(), surface.strip(), fixture_cell.strip(), status)


def normalize_path(path: str) -> str:
    """Normalize paths for comparison.

    - Strip trailing slashes.
    - Lower-case (ASP.NET routing is case-insensitive; INVENTORY uses lowercase
      verbs only inside the verb column, but the path column is already lowercased).
    - Normalize placeholder names: `{mangaId}` / `{name}` / `{id:int}` all collapse to
      `{id}` so we are comparing route SHAPES, not exact wording (callers care about
      "DELETE /api/v5/chapter/{anything}" matching).
    """
    p = path.rstrip("/").lower()
    # Collapse all `{anything}` to `{id}` so route placeholders compare by shape.
    p = re.sub(r"\{[^}]+\}", "{id}", p)
    return p


def main() -> int:
    if not INVENTORY.exists():
        print(f"ERROR: INVENTORY.md not found at {INVENTORY}", file=sys.stderr)
        return 2
    if not V5_DIR.exists():
        print(f"ERROR: V5 controller dir not found at {V5_DIR}", file=sys.stderr)
        return 2

    # 1. Walk V5 controllers, collect actual (verb, normalized-path) tuples.
    actual = set()
    actual_originals = {}  # (verb, normalized-path) -> original-path (for report)
    for cs in V5_DIR.rglob("*Controller.cs"):
        for verb, path in extract_controller_routes(cs):
            np = normalize_path(path)
            actual.add((verb, np))
            actual_originals.setdefault((verb, np), path)

    # 2. Walk INVENTORY v5-endpoint rows, collect catalogued (verb, normalized-path) tuples.
    inv_text = INVENTORY.read_text(encoding="utf-8")
    inventoried_rows = list(extract_inventory_v5_rows(inv_text))
    inventoried = set()
    inv_originals = {}
    for verb, path, surface, fixture_cell, status in inventoried_rows:
        np = normalize_path(path)
        inventoried.add((verb, np))
        inv_originals.setdefault((verb, np), path)

    # 3. Diff.
    missing_from_inv = sorted(actual - inventoried)
    drifted_in_inv = sorted(inventoried - actual)

    # 4. Emit report (UTF-8 safe for Windows cp1252 consoles).
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

    print("=== audit-inventory-endpoints.py ===")
    print(f"Actual V5 routes:        {len(actual):>4}")
    print(f"Catalogued in INVENTORY: {len(inventoried):>4}")
    print(f"Missing from INVENTORY:  {len(missing_from_inv):>4}")
    print(f"Drifted in INVENTORY:    {len(drifted_in_inv):>4}")
    print()
    if missing_from_inv:
        print("--- Missing from INVENTORY (actual route, no row) ---")
        for verb, np in missing_from_inv:
            orig = actual_originals.get((verb, np), np)
            print(f"  {verb:>6} {orig}")
        print()
    if drifted_in_inv:
        print("--- Drifted in INVENTORY (row exists, route gone) ---")
        for verb, np in drifted_in_inv:
            orig = inv_originals.get((verb, np), np)
            print(f"  {verb:>6} {orig}")
        print()
    return 1 if (missing_from_inv or drifted_in_inv) else 0


if __name__ == "__main__":
    sys.exit(main())
