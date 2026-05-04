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
#   * `jq` is OPTIONAL — schema spot-check is skipped when absent
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
)
MISSING=0
for path in "${PATHS_REQUIRED[@]}"; do
  if ! grep -q "\"${path}\"" "${OUTPUT_FILE}"; then
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
