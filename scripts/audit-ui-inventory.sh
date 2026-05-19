#!/usr/bin/env bash
# Phase 18 TEST-UI-01 coverage gate (D-03 four-axis inventory).
# Exits 0 when every row in `.planning/phases/18-.../INVENTORY.md` has a non-TBD
# `covering-test` AND every source axis (routes / endpoints / modals / requirements)
# is represented in INVENTORY.md AND no forbidden TV-shape testids leak into
# frontend/src.
#
# Gate failure modes:
#   1. TBD/MISSING row in INVENTORY.md           — Plan-10 Task 2 Gate 1
#   2. New route in AppRoutes.tsx not in INV     — Plan-10 Task 2 Gate 2
#   3. New TV-shape controller in Mangarr.Api.V5 — Plan-10 Task 2 Gate 3 (forbidden-pattern)
#   4. New TV-shape modal in frontend/src        — Plan-10 Task 2 Gate 4 (forbidden-pattern)
#   5. Forbidden TV-shape data-testid           — Plan-10 Task 2 Gate 5
#   6. Uncovered (⬜) row count > documented v2-deferred cap — Plan-19 Task 2 Gate 6
#      (cap raised when the table itself has been verified to reflect disk state
#       per the reconcile-inventory.py last-reconciled timestamp; prevents silent
#       drift where uncovered rows accumulate without explicit deferral entries.)
#
# Per .planning/phases/18-automated-ui-integration-test-suite-playwright-net/18-VALIDATION.md
# §"Meta-Validation: Smoke-the-Smokes" point 1.
#
# Gates 3 and 4 are FORBIDDEN-PATTERN checks (no Series/Episode/Season class/file
# names) rather than positive-coverage checks because INVENTORY.md is curated with
# cross-axis deduplication (modals deduplicate against endpoints, etc.). The
# positive-coverage gate is held by INVENTORY.md itself being checked in (D-03);
# the script's job is to prevent TV-shape leakage from re-introducing during
# future plan work. Known TV-shape residues from Phase 17.3 D-07 (GH #86) are
# explicitly allowlisted with a documented rename target.

set -euo pipefail

# gh172 — Gate 2's `path="..."` route-extraction pipeline. Used by the live
# Gate 2 loop (against frontend/src/App/AppRoutes.tsx) AND by `--self-test`
# (against an inline fixture). Defined once so a future change to the
# extraction logic cannot make `--self-test` pass while Gate 2 silently
# regresses (PR #193 review-fix).
extract_route_paths() {
  local src_file="$1"
  grep -oE 'path="[^"]+"' "$src_file" | sed 's/path="//;s/"$//' | sort -u
}

# gh172 — `--self-test` mode: lock in the Gate 2 contract that the `path="..."`
# extraction handles every `<Route>` declaration shape Prettier produces in this
# codebase. Built before the rest of the gate runs so a regex regression fails
# fast and locally (no need to push a phony AppRoutes change to exercise it).
#
# The current regex `grep -oE 'path="[^"]+"'` is line-oriented and therefore robust
# to multi-line `<Route ... />` declarations as long as the `path="..."` attribute
# value itself fits on one physical line — JSX string literals cannot span lines
# without explicit braces, so this holds for every Prettier-formatted Route in
# AppRoutes.tsx today (single-line, attribute-wrap, conditional-render, placeholder).
# This self-test exercises each shape so a future change that breaks the contract
# (e.g. switching the regex to a multi-line state machine that fumbles the
# conditional-render case) is caught in CI rather than as silent drift.
if [[ "${1:-}" == "--self-test" ]]; then
  fixture=$(mktemp -t audit-ui-inventory-self-test-XXXXXX.tsx 2>/dev/null || mktemp)
  trap "rm -f '$fixture'" EXIT INT TERM

  cat > "$fixture" <<'SELFTEST_FIXTURE_EOF'
// Self-test fixture for gh172 — exercises every <Route> formatting Prettier
// is known to produce in frontend/src/App/AppRoutes.tsx.
import { Route } from 'react-router-dom';

function Fixture() {
  return (
    <Switch>
      {/* 1. Single-line */}
      <Route path="/single-line" component={A} />

      {/* 2. Multi-line — attribute wrap (the common Prettier shape) */}
      <Route
        exact={true}
        path="/multi-line-wrap"
        component={B}
      />

      {/* 3. Multi-line inside a conditional-render block (urlBase redirect shape) */}
      {condition && (
        <Route
          exact={true}
          path="/conditional-render"
          render={X}
        />
      )}

      {/* 4. Placeholder path */}
      <Route
        exact={true}
        path="/placeholder/:slug"
        component={C}
      />

      {/* 5. Catch-all */}
      <Route path="*" component={NotFound} />
    </Switch>
  );
}
SELFTEST_FIXTURE_EOF

  actual=$(extract_route_paths "$fixture")
  # `sort -u` after construction so a future maintainer can reorder the literals
  # for readability without flipping the self-test result (PR #193 review-fix).
  expected=$(printf '%s\n' '*' '/conditional-render' '/multi-line-wrap' '/placeholder/:slug' '/single-line' | sort -u)

  if diff <(echo "$actual") <(echo "$expected") >/dev/null 2>&1; then
    echo "PASS: audit-ui-inventory.sh --self-test"
    echo "  Gate 2 regex correctly extracts all 5 fixture paths (single-line,"
    echo "  multi-line wrap, conditional-render, placeholder, catch-all)."
    echo "  Extracted: $(echo "$actual" | tr '\n' ' ')"
    exit 0
  else
    echo "FAIL: audit-ui-inventory.sh --self-test — Gate 2 regex mismatch" >&2
    echo "  Expected:" >&2
    echo "$expected" | sed 's/^/    /' >&2
    echo "  Actual:" >&2
    echo "$actual" | sed 's/^/    /' >&2
    exit 1
  fi
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
INV_DIR="$REPO_ROOT/.planning/phases/18-automated-ui-integration-test-suite-playwright-net"
INV_FILE="$INV_DIR/INVENTORY.md"
ROUTES_TSX="$REPO_ROOT/frontend/src/App/AppRoutes.tsx"
V5_DIR="$REPO_ROOT/src/Mangarr.Api.V5"
FRONTEND_DIR="$REPO_ROOT/frontend/src"

fail() { echo "FAIL: $*" >&2; exit 1; }

[[ -f "$INV_FILE" ]] || fail "INVENTORY.md missing: $INV_FILE"
[[ -f "$ROUTES_TSX" ]] || fail "AppRoutes.tsx missing: $ROUTES_TSX"
[[ -d "$V5_DIR" ]] || fail "Mangarr.Api.V5 dir missing: $V5_DIR"
[[ -d "$FRONTEND_DIR" ]] || fail "frontend/src dir missing: $FRONTEND_DIR"

# Known TV-shape residues from Phase 17.3 D-07 — pending rename per GH #86.
# These are the documented exceptions: do not add to this list without a tracking issue.
ALLOWED_TV_RESIDUE_MODALS=(
  "SelectEpisodeModal"
  "SelectSeasonModal"
  "SelectSeriesModal"
)
ALLOWED_TV_RESIDUE_PATHS=(
  "frontend/src/InteractiveImport/Episode/SelectEpisodeModal.tsx"
  "frontend/src/InteractiveImport/Season/SelectSeasonModal.tsx"
  "frontend/src/InteractiveImport/Series/SelectSeriesModal.tsx"
)

is_allowed_residue() {
  local needle="$1"
  for allowed in "${ALLOWED_TV_RESIDUE_MODALS[@]}" "${ALLOWED_TV_RESIDUE_PATHS[@]}"; do
    if [[ "$needle" == "$allowed" ]]; then
      return 0
    fi
  done
  return 1
}

# Gate 1 — no TBD covering-test in any axis row.
TBD_COUNT=$(grep -cE '\| (TBD|tbd|MISSING|missing) \|' "$INV_FILE" || true)
if [[ "$TBD_COUNT" -gt 0 ]]; then
  echo "FAIL: $TBD_COUNT inventory rows have TBD/MISSING covering-test:" >&2
  grep -nE '\| (TBD|tbd|MISSING|missing) \|' "$INV_FILE" >&2
  exit 1
fi
echo "PASS: Gate 1 — no TBD/MISSING rows in INVENTORY.md."

# Gate 2 — every route in AppRoutes.tsx is represented in INVENTORY.md (route axis).
MISSING_ROUTES=()
while IFS= read -r route; do
  [[ -z "$route" ]] && continue
  # The catch-all wildcard `*` and root `/` are special; INVENTORY rows reference
  # them in the `surface` column (e.g. "* (catch-all)" and "/ (urlBase redirect)").
  # Use literal grep — INVENTORY.md route axis must contain the exact path string.
  if ! grep -qF "| route | \`$route\`" "$INV_FILE"; then
    MISSING_ROUTES+=("$route")
  fi
done < <(extract_route_paths "$ROUTES_TSX")

if [[ ${#MISSING_ROUTES[@]} -gt 0 ]]; then
  echo "FAIL: ${#MISSING_ROUTES[@]} routes in AppRoutes.tsx not represented in INVENTORY.md route axis:" >&2
  printf '  - %s\n' "${MISSING_ROUTES[@]}" >&2
  echo 'Hint: each new route MUST have a `| route | `/path` | <surface> | <fixture> | <status> |` row.' >&2
  exit 1
fi
echo "PASS: Gate 2 — all $(extract_route_paths "$ROUTES_TSX" | wc -l) routes in AppRoutes.tsx represented in INVENTORY.md."

# Gate 3 — no TV-shape (Series/Episode/Season) controller class names in Mangarr.Api.V5.
# Phase 17.3 D-05/D-07 swept these; this gate prevents reintroduction.
TV_CONTROLLERS=()
while IFS= read -r ctrl_file; do
  [[ -z "$ctrl_file" ]] && continue
  ctrl_name=$(basename "$ctrl_file" .cs)
  if [[ "$ctrl_name" =~ ^(Series|Episode|Season) ]] || [[ "$ctrl_name" =~ (Series|Episode|Season)Controller$ ]]; then
    if ! is_allowed_residue "$ctrl_name"; then
      TV_CONTROLLERS+=("$ctrl_file")
    fi
  fi
done < <(find "$V5_DIR" -name "*Controller.cs" -not -path "*/bin/*" -not -path "*/obj/*")

if [[ ${#TV_CONTROLLERS[@]} -gt 0 ]]; then
  echo "FAIL: ${#TV_CONTROLLERS[@]} TV-shape controller(s) in Mangarr.Api.V5 (Phase 17.3 swept these — should not return):" >&2
  printf '  - %s\n' "${TV_CONTROLLERS[@]}" >&2
  exit 1
fi
echo "PASS: Gate 3 — no TV-shape controllers in Mangarr.Api.V5."

# Gate 4 — no NEW TV-shape modal files in frontend/src outside the documented residue allowlist.
TV_MODALS=()
while IFS= read -r modal_file; do
  [[ -z "$modal_file" ]] && continue
  modal_name=$(basename "$modal_file" .tsx)
  # Skip generic scaffolding under Components/Modal/
  if [[ "$modal_file" == *"/Components/Modal/"* ]]; then
    continue
  fi
  if [[ "$modal_name" =~ (Series|Episode|Season) ]]; then
    rel_path="${modal_file#$REPO_ROOT/}"
    if ! is_allowed_residue "$modal_name" && ! is_allowed_residue "$rel_path"; then
      TV_MODALS+=("$rel_path")
    fi
  fi
done < <(find "$FRONTEND_DIR" -name "*Modal.tsx" 2>/dev/null)

if [[ ${#TV_MODALS[@]} -gt 0 ]]; then
  echo "FAIL: ${#TV_MODALS[@]} NEW TV-shape modal(s) in frontend/src (Phase 17.3 D-07 swept; only documented residue allowed):" >&2
  printf '  - %s\n' "${TV_MODALS[@]}" >&2
  echo "Hint: rename to manga-shape (e.g. SelectChapterModal) OR add to ALLOWED_TV_RESIDUE_* with a tracking GH issue." >&2
  exit 1
fi
echo "PASS: Gate 4 — no NEW TV-shape modals (3 allowlisted residues from GH #86: SelectEpisodeModal, SelectSeasonModal, SelectSeriesModal)."

# Gate 5 — forbidden TV-shape data-testid strings in frontend/src.
# Filter out comments first to avoid self-invalidating-grep-gate (planner-antipatterns.md).
# WR-09 (20-REVIEW): expand the separator class to [-_] so underscore-separated
# TV-shape testids (e.g. data-testid="series_card") cannot slip through the gate.
FORBIDDEN_TESTID_HITS=$(grep -rnE 'data-testid="(series|episode|season|add-series)[-_]' "$FRONTEND_DIR" 2>/dev/null \
  | grep -vE '^[^:]+:[0-9]+:[[:space:]]*//' \
  | grep -vE '^[^:]+:[0-9]+:[[:space:]]*\*' \
  || true)

if [[ -n "$FORBIDDEN_TESTID_HITS" ]]; then
  HIT_COUNT=$(echo "$FORBIDDEN_TESTID_HITS" | wc -l)
  echo "FAIL: $HIT_COUNT forbidden TV-shape data-testid string(s) in frontend/src (per D-18 + Phase 17.3 audit):" >&2
  echo "$FORBIDDEN_TESTID_HITS" | head -20 >&2
  exit 1
fi
echo "PASS: Gate 5 — no forbidden TV-shape data-testid strings."

# Gate 6 — uncovered (⬜) row count must stay under the documented v2-deferred cap.
# Phase 20 close-out (2026-05-15): all 120 ⬜ rows greened or LiveService-tagged per D-08..D-10.
# Threshold is 0; any new ⬜ requires either (a) a fixture, OR (b) a deferred-items.md entry
# with a GH issue ref AND a UNCOVERED_CAP env override on the PR that adds the ⬜.
# Atomic-with-the-last-⬜-greening-commit invariant honored per Phase 20 CONTEXT D-11.
UNCOVERED_CAP=${UNCOVERED_CAP:-0}
UNCOVERED_ROWS=$(grep -cE '\| ⬜ \|' "$INV_FILE" || true)
if [[ "$UNCOVERED_ROWS" -gt "$UNCOVERED_CAP" ]]; then
  echo "FAIL: Gate 6 — $UNCOVERED_ROWS uncovered (⬜) rows in INVENTORY.md exceeds cap ($UNCOVERED_CAP)." >&2
  echo "Hint: either ship the fixture OR add a deferred-items.md entry with a GH issue ref." >&2
  echo "Hint: cap can be overridden via UNCOVERED_CAP env var for documented one-off bumps." >&2
  exit 1
fi
echo "PASS: Gate 6 — $UNCOVERED_ROWS uncovered (⬜) rows (cap: $UNCOVERED_CAP; tighten as fixtures land)."

echo
echo "INVENTORY.md coverage gates all GREEN — TEST-UI-01 satisfied."
exit 0
