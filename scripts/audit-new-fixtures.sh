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

# Frontend: extract data-testid values + route literals
TESTID_LIST=$(mktemp)
for tsx in $TOUCHED_FRONTEND; do
  [ -f "$tsx" ] || continue
  grep -oE "data-testid=['\"][^'\"]+['\"]" "$tsx" 2>/dev/null \
    | sed -E "s/data-testid=['\"]([^'\"]+)['\"]/\1/" >> "$TESTID_LIST" \
    || true
done

# De-duplicate candidate names
sort -u "$CANDIDATE_NAMES" -o "$CANDIDATE_NAMES"
sort -u "$TESTID_LIST" -o "$TESTID_LIST"

echo "==== Touched source files ===="
echo "Backend .cs (non-test):  $(echo "$TOUCHED_SRC_CS" | grep -c . || echo 0)"
echo "Frontend .ts/.tsx:        $(echo "$TOUCHED_FRONTEND" | grep -c . || echo 0)"
echo "Candidate type-names:    $(wc -l < "$CANDIDATE_NAMES")"
echo "Candidate testids:       $(wc -l < "$TESTID_LIST")"
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

echo "==== In-scope fixtures (NEW + EXISTING-touched) ===="
if [ -z "$IN_SCOPE" ]; then
  echo "  (none — empty phase touch or no fixture references)"
  echo ""
  echo "PASS: audit-new-fixtures (nothing to run)"
  exit 0
fi
echo "$IN_SCOPE" | sed 's/^/  /'
COUNT=$(echo "$IN_SCOPE" | wc -l)
echo "  ($COUNT fixtures)"
echo ""

if [ "$NO_RUN" = true ]; then
  echo "==== --no-run mode: skipping dotnet test execution ===="
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

RESULTS_FILE=$(mktemp)
EXIT_CODE=0

# --- Unit fixtures ---
if [ -n "$UNIT_FILTERS" ]; then
  echo "==== Running UNIT fixtures ===="
  echo "Filter: $UNIT_FILTERS"
  if ! dotnet test src/Mangarr.sln \
       --configuration Debug \
       --no-build \
       --filter "$UNIT_FILTERS" \
       --logger "console;verbosity=normal" 2>&1 | tee -a "$RESULTS_FILE"; then
    EXIT_CODE=1
  fi
  echo ""
fi

# --- Automation fixtures ---
# Pre-condition: port 8989 free. AutomationTest.cs runs its own NzbDroneRunner
# which calls KillAll() in [OneTimeSetUp] but a stale dev-mode dotnet on Windows
# can race the start. Wipe DB so the runner gets a clean baseline.
if [ -n "$AUTOMATION_FILTERS" ]; then
  echo "==== Running AUTOMATION (Playwright) fixtures ===="
  echo "Filter: $AUTOMATION_FILTERS"

  # Sanity: Playwright provisioning check
  if ! ls "$HOME/.cache/ms-playwright" >/dev/null 2>&1 && \
     ! ls "${LOCALAPPDATA:-$HOME/AppData/Local}/ms-playwright" >/dev/null 2>&1; then
    echo "FAIL: Playwright browsers not installed; cannot run Automation fixtures."
    echo "  Install via: cd src/NzbDrone.Automation.Test && pwsh bin/Debug/net10.0/playwright.ps1 install --with-deps chromium"
    EXIT_CODE=1
  else
    # DB wipe per feedback_db_wipe_no_backup
    rm -f /c/ProgramData/Mangarr/mangarr.db /c/ProgramData/Mangarr/mangarr.db-shm /c/ProgramData/Mangarr/mangarr.db-wal 2>/dev/null || true

    if ! dotnet test src/NzbDrone.Automation.Test/Mangarr.Automation.Test.csproj \
         --configuration Debug \
         --no-build \
         --filter "$AUTOMATION_FILTERS" \
         --logger "console;verbosity=normal" 2>&1 | tee -a "$RESULTS_FILE"; then
      EXIT_CODE=1
    fi
  fi
  echo ""
fi

# ---- Step 4: report ---------------------------------------------------------

if [ -n "$REPORT_PATH" ]; then
  cat > "$REPORT_PATH" <<EOF
{
  "phase_base": "$PHASE_BASE",
  "phase_tip":  "$(git rev-parse "$PHASE_TIP")",
  "scope_count": $COUNT,
  "new_count": $(echo "$NEW_FIXTURES" | grep -c . || echo 0),
  "in_scope_fixtures": [$(echo "$IN_SCOPE" | awk 'BEGIN{ORS=""}{if(NR>1)print ",";printf "\"%s\"", $0}END{print ""}')],
  "exit_code": $EXIT_CODE,
  "verdict": "$( [ $EXIT_CODE -eq 0 ] && echo PASS || echo FAIL )"
}
EOF
fi

echo "==== Verdict ===="
if [ $EXIT_CODE -eq 0 ]; then
  echo "PASS: audit-new-fixtures ($COUNT fixtures all executed + green)"
else
  echo "FAIL: audit-new-fixtures (one or more fixtures unexecuted, red, or compile-broken)"
fi

exit $EXIT_CODE
