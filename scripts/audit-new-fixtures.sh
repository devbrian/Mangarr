#!/usr/bin/env bash
# Phase 22 close-out structural fix — fixture-execution gate.
#
# Triggered by Phase 22 post-mortem (`mangarr-phase-smoke-test` ran 5 net-new
# Tag Playwright fixtures, 3 failed, but every other phase-close gate
# (`sonarr-consistency-audit`, `audit-ui-inventory.sh`, `audit-test-assertions.sh`,
# `audit-inventory-endpoints.py`, `gsd-verifier`) was GREEN — because none of
# them execute fixtures. They only verify structure or static source.
#
# This gate closes the recurring failure mode: phases declare "complete"
# while authored fixtures have never been run against a live app. Authoring
# defects (wrong expected status code, missing selector tolerance, stale
# DOM locator) propagate silently to phase-close status.
#
# Scope (per user directive 2026-05-17):
#   1. NEW fixtures — *Fixture.cs files added in the phase diff
#   2. EXISTING fixtures touching changed code — fixtures whose name pattern
#      matches the changed source file's basename, OR whose body references
#      a class/type defined in changed source files
#
# Gate behavior:
#   - GREEN exit 0 iff (a) every in-scope fixture compiled, (b) every in-scope
#     fixture executed, (c) every in-scope fixture passed.
#   - RED exit non-zero with structured failure list otherwise.
#
# Output format mirrors audit-ui-inventory.sh / audit-test-assertions.sh:
# the script emits PASS/FAIL lines to stdout and a structured JSON report
# (when --report=<path> is passed) for orchestrator integration.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

# ---- Argument parsing -------------------------------------------------------

PHASE_BASE=""
PHASE_TIP="HEAD"
REPORT_PATH=""
NO_RUN=false   # Self-test / dry-run: enumerate fixtures, skip dotnet test

while [ $# -gt 0 ]; do
  case "$1" in
    --base)        PHASE_BASE="$2";   shift 2 ;;
    --tip)         PHASE_TIP="$2";    shift 2 ;;
    --report)      REPORT_PATH="$2";  shift 2 ;;
    --no-run)      NO_RUN=true;       shift ;;
    -h|--help)
      cat <<EOF
Usage: audit-new-fixtures.sh [--base <sha>] [--tip <sha>] [--report <path>] [--no-run]

Identifies NEW fixtures (added in the phase diff) AND EXISTING fixtures that
exercise functionality touched by the phase diff, then runs each fixture with
\`dotnet test --filter\`. Fails if any fixture is unexecuted, red, or compile-broken.

Default base = merge-base of HEAD and Mangarr-v0.
Default tip  = HEAD.
EOF
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [ -z "$PHASE_BASE" ]; then
  PHASE_BASE=$(git merge-base "$PHASE_TIP" Mangarr-v0 2>/dev/null || git merge-base "$PHASE_TIP" main 2>/dev/null || true)
fi
if [ -z "$PHASE_BASE" ]; then
  echo "FATAL: could not derive phase base (no Mangarr-v0 or main branch)" >&2
  exit 2
fi

echo "==== audit-new-fixtures.sh ===="
echo "Phase base: $PHASE_BASE"
echo "Phase tip:  $PHASE_TIP"
echo "Range:      $(git rev-list --count "$PHASE_BASE..$PHASE_TIP") commits"
echo ""

# ---- Step 1: enumerate NEW fixtures (added in phase diff) -------------------

# A fixture is a *.cs file under src/**/Test*/ OR src/**/*.Test/ whose path
# ends in `Fixture.cs`. (Mangarr convention: NUnit fixtures end in Fixture.cs,
# whether unit or Playwright.)

NEW_FIXTURES=$(git diff --name-only --diff-filter=A "$PHASE_BASE..$PHASE_TIP" -- 'src/**/*Fixture.cs' 2>/dev/null | sort -u || true)

echo "==== NEW fixtures (added in phase diff) ===="
if [ -z "$NEW_FIXTURES" ]; then
  echo "  (none)"
else
  echo "$NEW_FIXTURES" | sed 's/^/  /'
fi
echo ""

# ---- Step 2: enumerate EXISTING fixtures touching changed code --------------

# For each MODIFIED .cs file in the phase diff (not under a Test project),
# find fixtures that reference the file's primary type by name.
#
# Heuristic: extract `public (class|interface|enum) Foo` declarations from
# the touched source file (excluding nested types), then grep all existing
# *Fixture.cs for `\bFoo\b`. Also pattern-match `FooFixture.cs` / `FooServiceFixture.cs`
# / `FooControllerFixture.cs` by name.
#
# For frontend files (*.tsx, *.ts), Playwright fixtures don't reference the
# source by class name — instead they navigate routes or use data-testid
# selectors. Heuristic: for each touched frontend file, extract literal
# data-testid values + route literals, then grep Playwright fixtures
# (src/NzbDrone.Automation.Test/Tests/) for those literals.

TOUCHED_SRC_CS=$(git diff --name-only "$PHASE_BASE..$PHASE_TIP" -- 'src/**/*.cs' 2>/dev/null \
  | grep -vE '/(Test|Tests)\.cs$|\.Test/|\.Test.csproj$|Fixture\.cs$' \
  | sort -u || true)

TOUCHED_FRONTEND=$(git diff --name-only "$PHASE_BASE..$PHASE_TIP" -- 'frontend/**/*.tsx' 'frontend/**/*.ts' 2>/dev/null \
  | grep -vE '\.d\.ts$|\.test\.|\.spec\.' \
  | sort -u || true)

# Collect candidate-name list for grep-based fixture discovery.
CANDIDATE_NAMES=$(mktemp)
TESTID_LIST=""
FIXTURE_LIST=""
RESULTS_FILE=""
trap 'rm -f "$CANDIDATE_NAMES" "${TESTID_LIST:-}" "${FIXTURE_LIST:-}" "${RESULTS_FILE:-}" 2>/dev/null || true' EXIT

for src in $TOUCHED_SRC_CS; do
  [ -f "$src" ] || continue
  # Extract `public (class|interface|enum|struct|record) Foo` names
  grep -oE 'public[[:space:]]+(static[[:space:]]+)?(partial[[:space:]]+)?(abstract[[:space:]]+|sealed[[:space:]]+)?(class|interface|enum|struct|record)[[:space:]]+[A-Z][A-Za-z0-9_]+' "$src" 2>/dev/null \
    | awk '{print $NF}' >> "$CANDIDATE_NAMES" || true
  # Also include the file's basename (e.g., TagController) — even if internal
  basename "$src" .cs >> "$CANDIDATE_NAMES" || true
done

# Frontend: extract data-testid values + route literals (PR #197 coderabbit
# follow-up: original implementation only harvested testids despite the
# docstring promising both. A Playwright fixture that navigates a touched
# route but doesn't reference any testid from the touched file would slip
# past bucket 4. Route-axis fixtures under Tests/Routes/*LoadFixture.cs are
# the canonical case — they navigate to the route and assert the page
# loads, with no testid grep-match to anchor scope discovery.)
TESTID_LIST=$(mktemp)
ROUTE_LIST=$(mktemp)
for tsx in $TOUCHED_FRONTEND; do
  [ -f "$tsx" ] || continue
  # Bucket 4a: data-testid values
  grep -oE "data-testid=['\"][^'\"]+['\"]" "$tsx" 2>/dev/null \
    | sed -E "s/data-testid=['\"]([^'\"]+)['\"]/\1/" >> "$TESTID_LIST" \
    || true
  # Bucket 4b-i: literal route strings inside the touched file
  #   path="/foo" / path='/foo'  (Route definitions)
  grep -oE "path=['\"]/[^'\"]+['\"]" "$tsx" 2>/dev/null \
    | sed -E "s/path=['\"]([^'\"]+)['\"]/\1/" >> "$ROUTE_LIST" || true
  #   to="/foo" / to='/foo'  (Link / NavLink targets)
  grep -oE "to=['\"]/[^'\"]+['\"]" "$tsx" 2>/dev/null \
    | sed -E "s/to=['\"]([^'\"]+)['\"]/\1/" >> "$ROUTE_LIST" || true
  #   to: '/foo'  (sidebar / config-object style)
  grep -oE "to:[[:space:]]*['\"]/[^'\"]+['\"]" "$tsx" 2>/dev/null \
    | sed -E "s/to:[[:space:]]*['\"]([^'\"]+)['\"]/\1/" >> "$ROUTE_LIST" || true
  #   navigate('/foo') / push('/foo') / replace('/foo')  (programmatic)
  grep -oE "(navigate|push|replace)\(['\"]/[^'\"]+['\"]" "$tsx" 2>/dev/null \
    | sed -E "s/[a-zA-Z]+\(['\"]([^'\"]+)['\"]/\1/" >> "$ROUTE_LIST" || true

  # Bucket 4b-ii: path-prefix derivation. The Sonarr/Mangarr frontend
  # convention is that `frontend/src/<Segment>/<Name>.tsx` (top-level page
  # component) is rendered at `/<segment>/<name>` (lowercased) via
  # AppRoutes.tsx. Only top-level page components qualify — modal-content
  # files like `frontend/src/Manga/Edit/EditMangaModalContent.tsx` live
  # under deeper paths and are NOT navigation targets, so we skip them.
  # The basename must match the parent dir name (Sonarr-canonical convention:
  # the page component file is named after its containing directory, e.g.
  # `Settings/Tags/Tags.tsx`, `Settings/Profiles/Quality/QualityProfile.tsx`).
  case "$tsx" in
    frontend/src/*/*/*.tsx)
      # Three-segment match: frontend/src/<Top>/<Sub>/<File>.tsx
      base=$(basename "$tsx" .tsx)
      parent=$(dirname "$tsx" | xargs basename)
      if [ "$base" = "$parent" ]; then
        # File matches its containing dir name → top-level page component.
        derived=$(echo "$tsx" | sed -E 's|^frontend/src/([^/]+)/([^/]+)/[^/]+\.tsx$|/\1/\2|' \
                              | tr '[:upper:]' '[:lower:]')
        if echo "$derived" | grep -qE '^/[a-z][a-z0-9-]{1,30}/[a-z][a-z0-9-]{1,30}$'; then
          echo "$derived" >> "$ROUTE_LIST"
        fi
      fi
      ;;
  esac
done

# De-duplicate candidate names
sort -u "$CANDIDATE_NAMES" -o "$CANDIDATE_NAMES"
sort -u "$TESTID_LIST" -o "$TESTID_LIST"
sort -u "$ROUTE_LIST" -o "$ROUTE_LIST"

echo "==== Touched source files ===="
echo "Backend .cs (non-test):  $(echo "$TOUCHED_SRC_CS" | grep -c . || echo 0)"
echo "Frontend .ts/.tsx:        $(echo "$TOUCHED_FRONTEND" | grep -c . || echo 0)"
echo "Candidate type-names:    $(wc -l < "$CANDIDATE_NAMES")"
echo "Candidate testids:       $(wc -l < "$TESTID_LIST")"
echo "Candidate routes:        $(wc -l < "$ROUTE_LIST")"
echo ""

# Find EXISTING fixtures referencing any candidate name or testid
FIXTURE_LIST=$(mktemp)

# All fixtures in the test tree (NEW + EXISTING):
ALL_FIXTURES=$(find src -name '*Fixture.cs' 2>/dev/null | sort -u)

# Bucket 1: NEW (already enumerated)
echo "$NEW_FIXTURES" >> "$FIXTURE_LIST"

# Bucket 2: name-match heuristic — fixture filename starts with any candidate name
if [ -s "$CANDIDATE_NAMES" ]; then
  while IFS= read -r name; do
    [ -z "$name" ] && continue
    echo "$ALL_FIXTURES" | grep -E "/${name}([A-Z][A-Za-z0-9_]*)?Fixture\.cs$" >> "$FIXTURE_LIST" 2>/dev/null || true
  done < "$CANDIDATE_NAMES"
fi

# Bucket 3: body-grep — fixtures referencing any candidate type name
if [ -s "$CANDIDATE_NAMES" ] && [ -n "$ALL_FIXTURES" ]; then
  for fix in $ALL_FIXTURES; do
    [ -f "$fix" ] || continue
    # Limit to first 2000 lines (perf) — fixtures larger than 2000 lines are pathological
    head -2000 "$fix" | grep -wFf "$CANDIDATE_NAMES" >/dev/null 2>&1 && echo "$fix" >> "$FIXTURE_LIST" || true
  done
fi

# Bucket 4: testid-grep — Playwright fixtures referencing any touched testid
if [ -s "$TESTID_LIST" ]; then
  for testid in $(cat "$TESTID_LIST"); do
    [ -z "$testid" ] && continue
    echo "$ALL_FIXTURES" | xargs grep -lF "$testid" 2>/dev/null >> "$FIXTURE_LIST" || true
  done
fi

# Bucket 4b: route-grep — Playwright fixtures referencing any route literal
# harvested from touched frontend files (PR #197 coderabbit follow-up).
# Pulls in route-axis Tests/Routes/*LoadFixture.cs that navigate to a touched
# component's route but don't grep-match by class or testid. Skips /api/*
# paths (those are API URLs already covered by bucket 3's class-name grep).
if [ -s "$ROUTE_LIST" ]; then
  for route in $(cat "$ROUTE_LIST"); do
    [ -z "$route" ] && continue
    case "$route" in
      /api/*) continue ;;
    esac
    echo "$ALL_FIXTURES" | xargs grep -lF "$route" 2>/dev/null >> "$FIXTURE_LIST" || true
  done
fi

# Bucket 5: path-proximity + filename-prefix heuristic.
# Why: Phase 20 TagListFixture / TagAddFixture / TagDetailPanelFixture must
# run when Phase 22 touches frontend/src/Settings/Tags/Tags.tsx, but they
# don't grep-match the touched files' candidate types (TagController etc.)
# nor share a directory segment, because they predate the type rename and
# live flat under Tests/Settings/. Filename-prefix matching catches the
# obvious "fixture named Tag*Fixture.cs sibling to a touched file under
# Tags/ or named Tag*/Tags*/useTags*" case.
for src in $TOUCHED_SRC_CS $TOUCHED_FRONTEND; do
  [ -f "$src" ] || continue
  base=$(basename "$src")
  base_no_ext="${base%.*}"   # strip extension
  # Strip leading lower-case prefix (e.g., `useTags` → `Tags`).
  base_no_ext=$(echo "$base_no_ext" | sed -E 's/^[a-z]+//')
  # Extract LEADING noun only (first capitalized word). This is the file's
  # primary domain noun (e.g. `TagController` → `Tag`, `MangaService` → `Manga`,
  # `EditMangaModalContent` → `Edit`). Only the leading noun gets the prefix
  # match — internal words like `In` from `TagInUseValidator` or `Manga` from
  # `EditMangaModalContent` would false-positive too widely.
  leading=$(echo "$base_no_ext" | sed -E 's/^([A-Z][a-z]+).*$/\1/')
  case "$leading" in
    Manga|App|Component|Helper|Store|Action|Reducer|Selector|Settings|System|Common|Core|Api|Page|Modal|Form|Input|Field|Button|Test|Spec|Service|Edit|Add|Move|Delete|Create|Save|Update|Fetch|Content|Item|List|Card|Cell|Row|Header|Footer|Section|Box|Group|Inputs|Pending|Changes|Default|State|Type|Config|Provider|Factory|Registry|Validator|Builder|Wrapper|Controller|In|Use|Of|At|On|To|For|By|With|From|The|A|An|Is|It|My|New|Old|Get|Set|Has|Pre|Post|Sub|Super|Auto|Manual)
      continue ;;
  esac
  # Match fixtures whose basename starts with the leading noun
  echo "$ALL_FIXTURES" | grep -E "/${leading}s?[A-Z][^/]*Fixture\.cs$|/${leading}Fixture\.cs$|/${leading}s?Fixture\.cs$" >> "$FIXTURE_LIST" 2>/dev/null || true
done

# Deduplicate, strip empty
IN_SCOPE=$(sort -u "$FIXTURE_LIST" | grep -v '^$' || true)

# write_report: emit the JSON artifact when --report was passed. Called from
# all 3 exit paths (empty-scope, --no-run, post-tests) so the --report contract
# is honored regardless of early-exit shape. Coderabbit Major on PR #197 retro:
# the previous shape only wrote the report from the post-test path, so callers
# passing --report could get a PASS exit code without the JSON ever appearing
# on disk.
write_report() {
  [ -z "${REPORT_PATH:-}" ] && return 0
  local scope_count="${1:-0}"
  local exit_code="${2:-0}"
  local scope_json=""
  if [ -n "${IN_SCOPE:-}" ]; then
    scope_json=$(echo "$IN_SCOPE" | awk 'BEGIN{ORS=""}{if(NR>1)print ",";printf "\"%s\"", $0}END{print ""}')
  fi
  # `grep -c .` exits 1 when zero matches AND outputs "0" to stdout, so a
  # naive `grep -c . || echo 0` double-emits "0\n0". Compute via empty-check
  # short-circuit to keep the JSON valid on the empty-scope exit path.
  local new_count=0
  if [ -n "${NEW_FIXTURES:-}" ]; then
    new_count=$(echo "$NEW_FIXTURES" | grep -c . || true)
  fi
  cat > "$REPORT_PATH" <<EOF
{
  "phase_base": "$PHASE_BASE",
  "phase_tip":  "$(git rev-parse "$PHASE_TIP")",
  "scope_count": $scope_count,
  "new_count": $new_count,
  "in_scope_fixtures": [$scope_json],
  "exit_code": $exit_code,
  "verdict": "$( [ "$exit_code" -eq 0 ] && echo PASS || echo FAIL )"
}
EOF
}

echo "==== In-scope fixtures (NEW + EXISTING-touched) ===="
if [ -z "$IN_SCOPE" ]; then
  echo "  (none — empty phase touch or no fixture references)"
  echo ""
  echo "PASS: audit-new-fixtures (nothing to run)"
  write_report 0 0
  exit 0
fi
echo "$IN_SCOPE" | sed 's/^/  /'
COUNT=$(echo "$IN_SCOPE" | wc -l)
echo "  ($COUNT fixtures)"
echo ""

if [ "$NO_RUN" = true ]; then
  echo "==== --no-run mode: skipping dotnet test execution ===="
  write_report "$COUNT" 0
  exit 0
fi

# ---- Step 3: run each fixture --------------------------------------------

# Categorize by test project (Unit vs Automation).
UNIT_FILTERS=""
AUTOMATION_FILTERS=""

for fix in $IN_SCOPE; do
  basename=$(basename "$fix" .cs)
  if echo "$fix" | grep -q 'NzbDrone.Automation.Test/'; then
    AUTOMATION_FILTERS="${AUTOMATION_FILTERS}|FullyQualifiedName~${basename}"
  else
    UNIT_FILTERS="${UNIT_FILTERS}|FullyQualifiedName~${basename}"
  fi
done

# Strip leading pipe
UNIT_FILTERS="${UNIT_FILTERS#|}"
AUTOMATION_FILTERS="${AUTOMATION_FILTERS#|}"

# Exclude live-network / manual-only categories, mirroring the canonical filters:
#   - scripts/test.sh:           Category!=ManualTest & Category!=LiveComix
#   - build_v5.yml PR-smoke (268) + automation (383): TestCategory!=LiveService
# LiveComix (unit-side) and LiveService (automation-side) fixtures hit the real
# internet (comix.to / MangaDex / AniList / MAL). They are non-deterministic,
# excluded from CI + the unit suite by design (see the fixture doc-comments + the
# mangarr-phase-smoke-test skill: "Don't run the full [Category(\"LiveService\")]
# tier — that's the nightly job, not the per-phase smoke"), and MUST NOT gate the
# smoke run: a transient comix.to Cloudflare challenge or bundle rotation would
# otherwise red the gate on an external condition, not a code regression. The
# name-OR group is parenthesized so the trailing AND binds to the whole set.
CATEGORY_EXCLUDE="Category!=LiveComix&Category!=LiveService&Category!=ManualTest"

RESULTS_FILE=$(mktemp)
EXIT_CODE=0

# --- Unit fixtures ---
# NOTE: do NOT pass --no-build here. The script header guarantees this gate fails
# on compile-broken fixtures; --no-build with stale binaries can produce a false
# PASS when current sources don't compile (codex P1 + coderabbit Major on PR #197).
#
# 2026-05-18 hang-fix: replaced `dotnet test ... | tee -a "$RESULTS_FILE"` with a
# direct `> "$RUN_LOG"` redirect + `cat`. On Windows, the `dotnet test` pipeline
# leaks process handles (testhost spawns helper processes that inherit the stdout
# pipe-write FD; `tee` never sees EOF after testhost exits) — observed on Phase 24
# smoke gate Run #1 and #2 (20+ min hang in post-test phase even after `Test Run
# Failed.` summary printed). The fix removes the pipe-to-tee pattern entirely:
# bash redirects dotnet test stdout/stderr directly to a file, then we cat it.
# No persistent pipe-write FD = no orphan-process handle leak.
if [ -n "$UNIT_FILTERS" ]; then
  echo "==== Running UNIT fixtures ===="
  UNIT_FILTER_EXPR="(${UNIT_FILTERS})&${CATEGORY_EXCLUDE}"
  echo "Filter: $UNIT_FILTER_EXPR"
  RUN_LOG=$(mktemp)
  if ! dotnet test src/Mangarr.sln \
       --configuration Debug \
       --filter "$UNIT_FILTER_EXPR" \
       --logger "console;verbosity=normal" > "$RUN_LOG" 2>&1; then
    EXIT_CODE=1
  fi
  cat "$RUN_LOG"
  cat "$RUN_LOG" >> "$RESULTS_FILE"
  rm -f "$RUN_LOG"
  echo ""
fi

# --- Automation fixtures ---
# Pre-condition: port 8989 free. AutomationTest.cs runs its own NzbDroneRunner
# which calls KillAll() in [OneTimeSetUp] but a stale dev-mode dotnet on Windows
# can race the start. Wipe DB so the runner gets a clean baseline.
if [ -n "$AUTOMATION_FILTERS" ]; then
  echo "==== Running AUTOMATION (Playwright) fixtures ===="
  AUTOMATION_FILTER_EXPR="(${AUTOMATION_FILTERS})&${CATEGORY_EXCLUDE}"
  echo "Filter: $AUTOMATION_FILTER_EXPR"

  # Sanity: Playwright provisioning check
  if ! ls "$HOME/.cache/ms-playwright" >/dev/null 2>&1 && \
     ! ls "${LOCALAPPDATA:-$HOME/AppData/Local}/ms-playwright" >/dev/null 2>&1; then
    echo "FAIL: Playwright browsers not installed; cannot run Automation fixtures."
    echo "  Install via: cd src/NzbDrone.Automation.Test && pwsh bin/Debug/net10.0/playwright.ps1 install --with-deps chromium"
    EXIT_CODE=1
  else
    # DB wipe per feedback_db_wipe_no_backup
    rm -f /c/ProgramData/Mangarr/mangarr.db /c/ProgramData/Mangarr/mangarr.db-shm /c/ProgramData/Mangarr/mangarr.db-wal 2>/dev/null || true

    # Cassette env applies ONLY to the AUTOMATION run — it configures the spawned
    # NzbDroneRunner backend to replay recorded cassettes (MangaDex HTTP layer +
    # Comix IComixSigner swap) offline, mirroring build_v5.yml's automation jobs.
    # It MUST NOT leak into the UNIT run above: the in-process CassetteHandler would
    # otherwise intercept real-HTTP unit fixtures (e.g. HttpClientFixture hitting
    # httpbin.servarr.com) and fail them on a cassette miss. CI keeps these in
    # separate jobs; this script runs both, so scope the env to this command only.
    # Unset unless the caller explicitly pre-set a value (don't clobber an override).
    AUTO_CASSETTE_MODE="${MANGARR_TEST_CASSETTE_MODE:-Replay}"
    AUTO_CASSETTE_DIR="${MANGARR_TEST_CASSETTE_DIR:-$(pwd)/src/NzbDrone.Automation.Test/Fixtures/Cassettes}"
    AUTO_CASSETTE_ASM="${MANGARR_TEST_ASSEMBLY_PATH:-$(pwd)/_tests/net10.0/Mangarr.Automation.Test.dll}"

    # See hang-fix note above for the no-pipe pattern. AUTOMATION fixtures spawn
    # Playwright browser worker node.exe processes — those PARTICULARLY tend to
    # leak the parent stdout pipe FD on Windows; this pattern bypasses the issue
    # entirely.
    RUN_LOG=$(mktemp)
    if ! MANGARR_TEST_CASSETTE_MODE="$AUTO_CASSETTE_MODE" \
         MANGARR_TEST_CASSETTE_DIR="$AUTO_CASSETTE_DIR" \
         MANGARR_TEST_ASSEMBLY_PATH="$AUTO_CASSETTE_ASM" \
         dotnet test src/NzbDrone.Automation.Test/Mangarr.Automation.Test.csproj \
         --configuration Debug \
         --filter "$AUTOMATION_FILTER_EXPR" \
         --logger "console;verbosity=normal" > "$RUN_LOG" 2>&1; then
      EXIT_CODE=1
    fi
    cat "$RUN_LOG"
    cat "$RUN_LOG" >> "$RESULTS_FILE"
    rm -f "$RUN_LOG"
  fi
  echo ""
fi

# ---- Step 4: report ---------------------------------------------------------
# Single canonical write via write_report() helper (defined earlier); all 3
# exit paths (empty-scope, --no-run, here) now honor the --report contract.

write_report "$COUNT" "$EXIT_CODE"

echo "==== Verdict ===="
if [ $EXIT_CODE -eq 0 ]; then
  echo "PASS: audit-new-fixtures ($COUNT fixtures all executed + green)"
else
  echo "FAIL: audit-new-fixtures (one or more fixtures unexecuted, red, or compile-broken)"
fi

exit $EXIT_CODE
