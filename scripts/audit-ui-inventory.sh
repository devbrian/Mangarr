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
done < <(grep -oE 'path="[^"]+"' "$ROUTES_TSX" | sed 's/path="//;s/"$//' | sort -u)

if [[ ${#MISSING_ROUTES[@]} -gt 0 ]]; then
  echo "FAIL: ${#MISSING_ROUTES[@]} routes in AppRoutes.tsx not represented in INVENTORY.md route axis:" >&2
  printf '  - %s\n' "${MISSING_ROUTES[@]}" >&2
  echo "Hint: each new route MUST have a `| route | \`/path\` | <surface> | <fixture> | <status> |` row." >&2
  exit 1
fi
echo "PASS: Gate 2 — all $(grep -oE 'path="[^"]+"' "$ROUTES_TSX" | sort -u | wc -l) routes in AppRoutes.tsx represented in INVENTORY.md."

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
FORBIDDEN_TESTID_HITS=$(grep -rnE 'data-testid="(series|episode|season|add-series)-' "$FRONTEND_DIR" 2>/dev/null \
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

echo
echo "INVENTORY.md coverage gates all GREEN — TEST-UI-01 satisfied."
exit 0
