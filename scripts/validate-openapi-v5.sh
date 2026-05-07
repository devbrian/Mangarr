#!/usr/bin/env bash
# Phase 7 — OpenAPI v5 spec validation pass per API-03.
#
# Sonarr divergence: NEW per Phase 7 Plan 07-12 — see DIVERGENCE.md.
# Source: 07-RESEARCH.md Lock #7 + Validation Architecture Wave 0 gap.
#
# What this validates:
#   1. /docs/v5/openapi.json is reachable on the running app (Debug build)
#   2. The JSON document is a structurally-valid OpenAPI 3.x spec
#      (npx --yes @apidevtools/swagger-cli validate)
#   3. Every Phase 2/5/6/7 manga + chapter endpoint appears in the paths block
#      (Pitfall 4 — [V5ApiController] route attribute coverage)
#   4. Spot-check 200 responses on manga/chapter endpoints carry a content
#      schema (Pitfall 4 — [Produces("application/json")] coverage)
#
# Prerequisites:
#   * App running in Debug mode at http://localhost:8989
#     (override via MANGARR_URL env var)
#     `dotnet run --project src/NzbDrone.Console/Sonarr.Console.csproj --configuration Debug`
#   * Node.js available for `npx`
#   * `jq` is REQUIRED for endpoint-coverage check (path lookup uses
#     `jq -e '.paths | has(...)'` for accuracy — see WR-02 fix)
#   * Bash shell — on Windows this means Git Bash, WSL, or a similar POSIX
#     environment. PowerShell is the default Windows shell per CLAUDE.md but
#     this script is bash-only (uses `set -euo pipefail`, parameter expansion,
#     `[[ ]]` test syntax, and `command -v`). Run as:
#       Git Bash:  bash scripts/validate-openapi-v5.sh
#       WSL:       bash scripts/validate-openapi-v5.sh
#     A PowerShell port (validate-openapi-v5.ps1) is a Phase 8 follow-up.
#
# Usage:
#   bash scripts/validate-openapi-v5.sh
#
# Exit codes:
#   0  validation passed
#   1  fetch failed (app not running in Debug mode?)
#   2  swagger-cli validation failed (malformed OpenAPI JSON)
#   3  required endpoint missing from paths block

set -euo pipefail

MANGARR_URL="${MANGARR_URL:-http://localhost:8989}"
OUTPUT_FILE="${TMPDIR:-/tmp}/mangarr-openapi-v5.json"

echo "Fetching OpenAPI v5 spec from ${MANGARR_URL}/docs/v5/openapi.json ..."
if ! curl -sf -o "${OUTPUT_FILE}" "${MANGARR_URL}/docs/v5/openapi.json"; then
  echo "ERROR: failed to fetch OpenAPI JSON from ${MANGARR_URL}/docs/v5/openapi.json"
  echo "       Is the app running in Debug mode? (Startup.cs:364 IsDebug guard)"
  exit 1
fi

echo "Validating ..."
if ! npx --yes @apidevtools/swagger-cli validate "${OUTPUT_FILE}"; then
  echo "ERROR: OpenAPI v5 JSON failed swagger-cli validation"
  exit 2
fi

# Lock #7 step 4: verify every manga/* and chapter endpoint appears in the
# paths block. The list below is the union of Phase 2 (CRUD/lookup),
# Phase 5 (translationprofile / customformatprofile / config/manganaming),
# Phase 6 (queue / history / blocklist / wanted/missing / release), and
# Phase 7 Plan 01 (chapter).
echo "Checking endpoint coverage ..."
# WR-02 fix: jq is required here so the check looks inside the paths block
# specifically. Substring grep would false-positive on path names that appear
# inside description/summary text without being routed.
if ! command -v jq >/dev/null 2>&1; then
  echo "ERROR: jq is required for endpoint coverage check"
  echo "       Install jq (https://stedolan.github.io/jq/) and re-run."
  exit 3
fi
PATHS_REQUIRED=(
  "/api/v5/manga"
  "/api/v5/manga/lookup"
  "/api/v5/manga/queue"
  "/api/v5/manga/history"
  "/api/v5/manga/blocklist"
  "/api/v5/manga/wanted/missing"
  "/api/v5/manga/release"
  "/api/v5/translationprofile"
  "/api/v5/customformatprofile"
  "/api/v5/config/manganaming"
  "/api/v5/chapter"
  # Phase 12 retrofit (Plan 12-12 — MangaCutoffController shipped /manga/wanted/cutoff
  # but the script's PATHS_REQUIRED was not updated at Phase 12 close-out per RESEARCH
  # §"Hardcoded PATHS_REQUIRED list incomplete relative to Phase 12 deliveries". Plan
  # 13-99 batch-update closes this gap.)
  "/api/v5/manga/wanted/cutoff"
  # Phase 13 backfills (Plans 13-04..13-10 — sub-wave C V5 surface backfill per
  # 13-API-V5-SURFACE-FINDINGS.md §1 + §2 + Cross-Section Reconciliation
  # forward-prophylactic gap_in_scope rows per D-13-04). Batch-update at sub-wave E
  # close-out per RESEARCH Open Question §1 answer.
  "/api/v5/manga/editor"               # Plan 13-04 — MangaEditorController PUT bulk-edit + DELETE bulk-delete
  "/api/v5/manga/{id}/folder"          # Plan 13-05 — MangaFolderController GET folder-name preview
  "/api/v5/manga/rename"               # Plan 13-06 — RenameChapterController GET single-manga rename preview
  "/api/v5/manga/rename/bulk"          # Plan 13-06 — RenameChapterController GET bulk rename preview
  "/api/v5/chapterFile"                # Plan 13-07 — ChapterFileController CRUD + SignalR (auto-derived camelCase route per useApiQuery key '/chapterFile' contract)
  "/api/v5/manga/queue/details"        # Plan 13-08 — MangaQueueDetailsController GET queue+pending concat with subresource hydration
  "/api/v5/manga/queue/status"         # Plan 13-09 — MangaQueueStatusController GET debounced 5s broadcast counters
  "/api/v5/manga/queue/grab/{id}"      # Plan 13-10 — MangaQueueActionController POST single-grab via FindPendingQueueItem + RemoteChapter shim
  "/api/v5/manga/queue/grab/bulk"      # Plan 13-10 — MangaQueueActionController POST bulk-grab on QueueBulkResource Ids list
  "/api/v5/manga/queue/{id}"           # Phase 6 — MangaQueueController DELETE-by-id (RestDeleteById) — listed for completeness per Plan 13-99 objective
  "/api/v5/manga/queue/bulk"           # Phase 6 — MangaQueueController bulk DELETE — listed for completeness per Plan 13-99 objective
)
MISSING=0
for path in "${PATHS_REQUIRED[@]}"; do
  if ! jq -e ".paths | has(\"${path}\")" "${OUTPUT_FILE}" >/dev/null 2>&1; then
    echo "MISSING: ${path}"
    MISSING=$((MISSING + 1))
  fi
done

if [[ ${MISSING} -gt 0 ]]; then
  echo "ERROR: ${MISSING} required endpoint(s) absent from /docs/v5/openapi.json"
  exit 3
fi

# Lock #7 step 5: verify every endpoint has a 200 response with content schema
# (Pitfall 4 — [Produces("application/json")] coverage). This step is
# advisory-only (does NOT fail the build) because Sonarr's existing v5
# endpoints may have similar gaps unrelated to manga work.
if command -v jq >/dev/null 2>&1; then
  echo "Spot-checking response schemas (advisory) ..."
  ENDPOINTS_WITHOUT_SCHEMA=$(jq -r '
    .paths | to_entries[] |
    select(.key | test("^/api/v5/(manga|chapter|translationprofile|customformatprofile)")) |
    .value | to_entries[] |
    select(.value.responses?."200" != null) |
    select(.value.responses."200".content == null) |
    "\(.key) on \(.value.operationId // "(unknown)")"
  ' "${OUTPUT_FILE}" 2>/dev/null || true)

  if [[ -n "${ENDPOINTS_WITHOUT_SCHEMA}" ]]; then
    echo "WARNING: endpoints with 200 response but no content schema (missing [Produces]?):"
    echo "${ENDPOINTS_WITHOUT_SCHEMA}"
    # Don't fail — Sonarr's existing v5 endpoints may have similar gaps. Log only.
  fi
else
  echo "Skipping response-schema spot-check (jq not installed; advisory-only step)"
fi

echo "OpenAPI v5 validation PASSED"
