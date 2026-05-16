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
  0 = no drift in catalogued rows (missing-from-INVENTORY is informational only;
      INVENTORY's "most specific axis" dedup rule means many backend-only by-id
      routes are intentionally subsumed by their modal-action row).
  1 = drifted-in-INVENTORY detected — a catalogued row points to a route that
      no longer exists on disk. Wave-2/3 cluster plans must not author fixtures
      against a stale path. This is the primary D-12 gate condition.

The Missing-from-INVENTORY block is always emitted (informational signal) so
future phases can spot newly-introduced user-visible routes that warrant a row;
it does NOT fail the gate.
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
# WR-05 (20-REVIEW): match any non-pipe sequence for the status column rather than
# a fixed emoji whitelist — rows authored with ⚠️ / ✅ / 🚧 / arbitrary annotation
# emojis would otherwise silently DROP from inventoried_rows, under-reporting the
# drifted-in-INVENTORY set and producing false-passing gates. A warning is emitted
# at parse time when an unknown status emoji is seen so authors notice drift early.
V5_ROW = re.compile(
    r"^\| v5-endpoint \| (\w+) ([^|]+?) \| ([^|]+?) \| (`[^|]+`) \| ([^|]+?) \|",
    re.MULTILINE,
)
KNOWN_STATUS_EMOJIS = {"⬜", "🟢", "🟡", "🔴"}

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

# Generic parent extraction for controllers that inherit HTTP attributes from a
# non-Provider abstract base (e.g. LogFileControllerBase). Captures the parent
# identifier so the caller can locate the base file and parse its [Http*] attrs.
PARENT_CLASS = re.compile(
    r"class\s+\w+Controller\s*:\s*([A-Za-z_][\w]*)"
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

    Priority (highest first):
      1. `[V5ApiController("literal")]`         -> return literal verbatim.
      2. `[V5ApiController]` + ProviderControllerBase ctor `: base(..., "<r>", ...)`
                                                -> return the ctor literal "<r>".
      3. `[V5ApiController]` fallback           -> class name minus "Controller", lower-cased.

    The path-3 fallback contract is grounded in ASP.NET's `[controller]` route token:
    when `VersionedApiControllerAttribute` is constructed with the default resource
    `[controller]` (src/Mangarr.Http/VersionedApiControllerAttribute.cs:11), the token
    expands at route-binding time to the controller class name with the trailing
    `Controller` suffix stripped, case-preserved. ASP.NET URL matching is
    case-insensitive, and INVENTORY rows are authored in lowercase, so
    `class_name.lower()` is the canonical comparable form. This holds for multi-word
    controllers as well — e.g. `CustomFormatProfileController` -> `customformatprofile`
    matches the route `/api/v5/customformatprofile` registered for that controller
    (gh171: explicit coverage added via `--self-test` for the multi-word fallback).

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


def _collect_base_class_attrs(parent_id: str) -> list[tuple[str, str]]:
    """Look up `<parent_id>.cs` anywhere under V5_DIR and parse its [Http*] attrs.

    Returns a list of (verb, sub_path) tuples. Empty list if the base file is not
    found or carries no HTTP attributes. Used for non-Provider abstract bases like
    LogFileControllerBase where the concrete controller inherits routes without
    re-declaring them.

    WR-07 (20-REVIEW): rglob matches every file whose basename equals `<parent_id>.cs`
    regardless of namespace. If two abstract bases share a basename in different
    subdirectories, routes from BOTH would silently merge. Emit a stderr warning
    when multiple matches are found so the author can disambiguate (full
    namespace-aware resolution would require parsing `using` directives — heavy).
    """
    candidates = list(V5_DIR.rglob(f"{parent_id}.cs"))
    if len(candidates) > 1:
        print(
            f"WARN: _collect_base_class_attrs found {len(candidates)} files matching "
            f"'{parent_id}.cs' under {V5_DIR}; routes from ALL are being merged "
            f"(no namespace resolution). Candidates: "
            f"{[str(c.relative_to(V5_DIR)) for c in candidates]}",
            file=sys.stderr,
        )
    routes = []
    for candidate in candidates:
        try:
            base_src = strip_csharp_comments(candidate.read_text(encoding="utf-8"))
        except OSError:
            continue
        for m in HTTP_ATTR.finditer(base_src):
            verb = m.group(1).upper()
            sub = m.group(2) or ""
            routes.append((verb, sub))
    return routes


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

    own_http = list(HTTP_ATTR.finditer(src))
    own_rest = list(REST_BY_ID.finditer(src))

    # 1. Explicit [HttpGet/Post/Put/Delete("...")] or bare [Http*].
    for m in own_http:
        verb = m.group(1).upper()
        sub = m.group(2) or ""
        path = f"{base}/{sub}".rstrip("/") if sub else base
        yield (verb, path)

    # 2. [RestPostById] / [RestPutById] / [RestDeleteById] on the controller itself.
    for m in own_rest:
        attr = m.group(1)
        verb, sub = REST_BY_ID_MAP[attr]
        path = f"{base}/{sub}".rstrip("/") if sub else base
        yield (verb, path)

    # 3. ProviderControllerBase inherited routes — only if the class extends it.
    is_provider = bool(PROVIDER_BASE_PARENT.search(src))
    if is_provider:
        for verb, sub in PROVIDER_BASE_ROUTES:
            path = f"{base}/{sub}".rstrip("/") if sub else base
            yield (verb, path)

    # 4. Non-Provider abstract base: when the class itself declares NO HTTP or
    #    REST-by-id attributes AND inherits from something other than the bare
    #    `Controller` / `RestController*` / `ProviderControllerBase`, try to locate
    #    the parent .cs file and parse its [Http*] attrs. Closes the LogFileController
    #    -> LogFileControllerBase inheritance gap.
    if not own_http and not own_rest and not is_provider:
        parent_match = PARENT_CLASS.search(src)
        if parent_match:
            parent_id = parent_match.group(1)
            # Skip well-known framework bases that ship no HTTP attrs of their own.
            if parent_id not in {
                "Controller",
                "ControllerBase",
                "RestController",
                "RestControllerWithSignalR",
            }:
                for verb, sub in _collect_base_class_attrs(parent_id):
                    path = f"{base}/{sub}".rstrip("/") if sub else base
                    yield (verb, path)


def extract_inventory_v5_rows(inv_text: str):
    """Yield (verb, path, surface, fixture_cell, status) for each v5-endpoint row.

    The fixture cell is returned verbatim (including backticks) so the caller can keep
    multi-fixture rows joined via ` + ` intact when reporting drift.

    WR-05 (20-REVIEW): warn (to stderr) on rows whose status cell does not contain
    any of KNOWN_STATUS_EMOJIS so a typo or new emoji surfaces immediately rather
    than silently passing the gate. We do not drop the row — the gate is route-based,
    not status-based — but the author gets a visible breadcrumb.
    """
    for m in V5_ROW.finditer(inv_text):
        verb, path, surface, fixture_cell, status = m.groups()
        status = status.strip()
        if not any(emoji in status for emoji in KNOWN_STATUS_EMOJIS):
            print(
                f"WARN: v5-endpoint row has unknown status '{status}' "
                f"(expected one of {sorted(KNOWN_STATUS_EMOJIS)}): "
                f"{verb} {path.strip()}",
                file=sys.stderr,
            )
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


def _self_test() -> int:
    """gh171 — exercise `derive_resource()` across all 3 derivation paths.

    Invoked via `python scripts/audit-inventory-endpoints.py --self-test`. Runs in
    process (no pytest dependency) so it can be wired into the same CI lane as the
    audit gate itself. Exits 0 on pass, 1 on first failure.

    Coverage:
      - Path 1: `[V5ApiController("literal")]` literal override.
      - Path 2: `[V5ApiController]` + ProviderControllerBase ctor literal.
      - Path 3: `[V5ApiController]` fallback (class name minus "Controller", lowercased)
                including single-word, multi-word, and acronym-bearing class names —
                the explicit gh171 coverage requested.
    """
    cases: list[tuple[str, str, str, str | None]] = [
        # (description, class_name, source_snippet, expected_resource)

        # Path 1 — explicit literal override.
        (
            "literal override",
            "MangaLookup",
            '[V5ApiController("manga/lookup")]\npublic class MangaLookupController : RestController<X>',
            "manga/lookup",
        ),
        (
            "literal override with hyphen",
            "ImportListExclusion",
            '[V5ApiController("importlist/exclusions")]\npublic class ImportListExclusionController : Controller',
            "importlist/exclusions",
        ),

        # Path 2 — ProviderControllerBase ctor literal.
        (
            "provider ctor literal — indexer",
            "Indexer",
            (
                "[V5ApiController]\n"
                "public class IndexerController : ProviderControllerBase<IndexerResource, IndexerBulkResource, IIndexer, IndexerDefinition>\n"
                "{\n"
                "    public IndexerController(b, factory, broadcaster)\n"
                '        : base(broadcaster, factory, "indexer", ResourceMapper, BulkResourceMapper) { }\n'
                "}"
            ),
            "indexer",
        ),
        (
            "provider ctor literal — multi-segment",
            "ImportList",
            (
                "[V5ApiController]\n"
                "public class ImportListController : ProviderControllerBase<ImportListResource, ImportListBulkResource, IImportList, ImportListDefinition>\n"
                '    : base(broadcaster, factory, "importlist", Mapper, BulkMapper) { }'
            ),
            "importlist",
        ),

        # Path 3 — class-name fallback (the gh171 focus area).
        (
            "fallback single-word",
            "Manga",
            "[V5ApiController]\npublic class MangaController : RestController<MangaResource>",
            "manga",
        ),
        (
            "fallback two-word",
            "CustomFormat",
            "[V5ApiController]\npublic class CustomFormatController : RestController<CustomFormatResource>",
            "customformat",
        ),
        (
            "fallback three-word (multi-word coverage)",
            "CustomFormatProfile",
            "[V5ApiController]\npublic class CustomFormatProfileController : RestController<CustomFormatProfileResource>",
            "customformatprofile",
        ),
        (
            "fallback four-word",
            "RemotePathMapping",
            "[V5ApiController]\npublic class RemotePathMappingController : RestController<RemotePathMappingResource>",
            "remotepathmapping",
        ),
        (
            "fallback with embedded acronym",
            "IndexerFlag",
            "[V5ApiController]\npublic class IndexerFlagController : RestController<IndexerFlagResource>",
            "indexerflag",
        ),

        # Negative — class without [V5ApiController] returns None.
        (
            "no V5 attribute",
            "Plain",
            "public class PlainController : Controller { }",
            None,
        ),
    ]

    failed = 0
    for desc, class_name, src, expected in cases:
        actual = derive_resource(class_name, src)
        ok = actual == expected
        status = "PASS" if ok else "FAIL"
        print(f"  [{status}] {desc}: derive_resource({class_name!r}, …) -> {actual!r} (expected {expected!r})")
        if not ok:
            failed += 1

    print()
    if failed:
        print(f"FAIL: {failed}/{len(cases)} self-test case(s) failed.", file=sys.stderr)
        return 1
    print(f"PASS: all {len(cases)} self-test case(s).")
    return 0


def main() -> int:
    if "--self-test" in sys.argv[1:]:
        return _self_test()

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
    print(f"Missing from INVENTORY:  {len(missing_from_inv):>4}  (informational)")
    print(f"Drifted in INVENTORY:    {len(drifted_in_inv):>4}  (gate)")
    print()
    if missing_from_inv:
        print("--- Missing from INVENTORY (actual route, no row) ---")
        print("    Informational: many of these are subsumed by their modal-action")
        print("    row per INVENTORY's most-specific-axis dedup rule (line 12).")
        for verb, np in missing_from_inv:
            orig = actual_originals.get((verb, np), np)
            print(f"  {verb:>6} {orig}")
        print()
    if drifted_in_inv:
        print("--- Drifted in INVENTORY (row exists, route gone) — GATE FAILURE ---")
        for verb, np in drifted_in_inv:
            orig = inv_originals.get((verb, np), np)
            print(f"  {verb:>6} {orig}")
        print()
    # Gate semantics per D-12: only DRIFTED entries (catalogued -> route gone) fail.
    # Missing-from-INVENTORY is informational because INVENTORY's "most specific axis"
    # dedup rule (line 12) means many actual routes are intentionally subsumed by
    # their modal-action row and don't need a standalone v5-endpoint row.
    return 1 if drifted_in_inv else 0


if __name__ == "__main__":
    sys.exit(main())
