#!/usr/bin/env bash
# scripts/phase-smoke-gate.sh
#
# Mechanical enforcement of mangarr-phase-smoke-test Tasks 1 + 2.5.
# Runs the unit suite + the fixture-execution gate, writes a structured
# SMOKE-GATE.json artifact into the phase dir, exits non-zero on any failure.
#
# Why this exists: Phase 20/22/23 retros — textual "MUST run" / "FATAL on miss"
# in the skill body did not prevent the orchestrator from rationalizing skips
# ("CI handles this", "executor deferred this", "build is the proxy gate").
# This script converts the contract into a machine-checkable artifact: either
# SMOKE-GATE.json exists with passed=true, or the phase cannot be declared
# verified. See CLAUDE.md "Mandatory smoke gate before phase verification"
# and the user memory feedback_never_defer_smoke_fixture_gate.
#
# Usage:
#   bash scripts/phase-smoke-gate.sh <phase-number>
#
# Exit codes:
#   0  — all gates passed; SMOKE-GATE.json written with passed=true
#   1  — at least one gate failed; SMOKE-GATE.json written with passed=false
#   2  — usage error or phase dir not found

set -uo pipefail

PHASE="${1:-}"
if [ -z "$PHASE" ]; then
  echo "Usage: $0 <phase-number>" >&2
  exit 2
fi

PHASE_DIR=$(ls -d .planning/phases/${PHASE}-* 2>/dev/null | head -1)
if [ -z "$PHASE_DIR" ] || [ ! -d "$PHASE_DIR" ]; then
  echo "FATAL: phase dir not found for phase $PHASE (looked for .planning/phases/${PHASE}-*)" >&2
  exit 2
fi

GATE_JSON="${PHASE_DIR}/SMOKE-GATE.json"
LOG_DIR="${PHASE_DIR}/smoke-gate-logs"
mkdir -p "$LOG_DIR"
TS=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

# State accumulator — JSON-encoded as we go.
PASSED="false"
PLAYWRIGHT_OK="null"
PORT_OK="null"
UNIT_OK="null"
UNIT_TOTAL="null"
UNIT_PASSED="null"
UNIT_FAILED="null"
UNIT_SKIPPED="null"
FIXTURE_OK="null"
FIXTURE_EXIT="null"
FAILURE_REASONS=()

write_gate() {
  local end_ts
  end_ts=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
  local reasons_json
  if [ ${#FAILURE_REASONS[@]} -gt 0 ]; then
    reasons_json=$(printf '"%s",' "${FAILURE_REASONS[@]}")
    reasons_json="[${reasons_json%,}]"
  else
    reasons_json="[]"
  fi
  cat > "$GATE_JSON" <<EOF
{
  "phase": "${PHASE}",
  "phase_dir": "${PHASE_DIR}",
  "started_at": "${TS}",
  "ended_at": "${end_ts}",
  "passed": ${PASSED},
  "failure_reasons": ${reasons_json},
  "playwright_provisioned": ${PLAYWRIGHT_OK},
  "port_8989_free_at_start": ${PORT_OK},
  "unit_suite": {
    "passed": ${UNIT_OK},
    "total": ${UNIT_TOTAL},
    "ok": ${UNIT_PASSED},
    "failed": ${UNIT_FAILED},
    "skipped": ${UNIT_SKIPPED},
    "log": "${LOG_DIR}/unit-suite.txt"
  },
  "fixture_gate": {
    "passed": ${FIXTURE_OK},
    "exit_code": ${FIXTURE_EXIT},
    "log": "${LOG_DIR}/audit-new-fixtures.txt",
    "report": "${LOG_DIR}/audit-new-fixtures-report.json"
  }
}
EOF
}
trap 'write_gate' EXIT

echo "=== phase-smoke-gate $PHASE — start $TS ==="

# Step 1: Playwright provisioned (FATAL if missing per Phase 22 mandatory contract)
echo "--- Step 1: Playwright provisioned"
if ! ls _tests/net10.0/playwright.ps1 >/dev/null 2>&1; then
  echo "FATAL: _tests/net10.0/playwright.ps1 not found." >&2
  echo "  Install: pwsh _tests/net10.0/playwright.ps1 install --with-deps chromium" >&2
  PLAYWRIGHT_OK="false"
  FAILURE_REASONS+=("playwright_not_provisioned")
  exit 1
fi
PLAYWRIGHT_OK="true"
echo "PASS Playwright provisioned"

# Step 2: Port 8989 free (the fixture runner spawns NzbDroneRunner on 8989)
echo "--- Step 2: Port 8989 free"
if netstat -ano 2>/dev/null | grep -qE "(0\.0\.0\.0|\[::\]):8989.*LISTENING"; then
  echo "FATAL: port 8989 is occupied; smoke gate cannot bind." >&2
  echo "  Kill the existing listener (e.g. taskkill /F /PID <pid>) and re-run." >&2
  PORT_OK="false"
  FAILURE_REASONS+=("port_8989_occupied")
  exit 1
fi
PORT_OK="true"
echo "PASS Port 8989 free"

# Step 3: Unit suite — scripts/test.sh always returns 0; parse Passed!/Failed! lines.
echo "--- Step 3: scripts/test.sh Windows Unit Test"
export TEST_DIR="./_tests/net10.0"
UNIT_LOG="${LOG_DIR}/unit-suite.txt"
bash scripts/test.sh Windows Unit Test > "$UNIT_LOG" 2>&1
# Per-DLL summary lines look like:
#   Passed!  - Failed:     0, Passed:   573, Skipped:    16, Total:   589, Duration: 45 s - Mangarr.Common.Test.dll (net10.0)
#   Failed!  - Failed:     3, Passed:   570, Skipped:    16, Total:   589, Duration: 45 s - Mangarr.Common.Test.dll (net10.0)
SUMMARY_LINES=$(grep -E "^(Passed|Failed)!" "$UNIT_LOG" || true)
if [ -z "$SUMMARY_LINES" ]; then
  echo "FAIL unit suite: no Passed!/Failed! lines found in log" >&2
  UNIT_OK="false"
  FAILURE_REASONS+=("unit_suite_no_summary_lines")
else
  UNIT_TOTAL=$(echo "$SUMMARY_LINES" | awk -F'Total:' '{print $2}' | awk '{print $1}' | sed 's/,$//' | awk '{s+=$1} END{print s+0}')
  UNIT_PASSED=$(echo "$SUMMARY_LINES" | awk -F'Passed:' '{print $2}' | awk '{print $1}' | sed 's/,$//' | awk '{s+=$1} END{print s+0}')
  UNIT_FAILED=$(echo "$SUMMARY_LINES" | awk -F'Failed:' '{print $2}' | awk '{print $1}' | sed 's/,$//' | awk '{s+=$1} END{print s+0}')
  UNIT_SKIPPED=$(echo "$SUMMARY_LINES" | awk -F'Skipped:' '{print $2}' | awk '{print $1}' | sed 's/,$//' | awk '{s+=$1} END{print s+0}')
  FAILED_LINE_COUNT=$(echo "$SUMMARY_LINES" | grep -cE "^Failed!" || true)
  if [ "$UNIT_FAILED" -gt 0 ] || [ "$FAILED_LINE_COUNT" -gt 0 ]; then
    echo "FAIL unit suite: $UNIT_FAILED failed of $UNIT_TOTAL total (across $FAILED_LINE_COUNT failed DLLs)" >&2
    UNIT_OK="false"
    FAILURE_REASONS+=("unit_suite_failures:${UNIT_FAILED}")
  else
    echo "PASS unit suite: $UNIT_PASSED passed / $UNIT_FAILED failed / $UNIT_SKIPPED skipped (total $UNIT_TOTAL)"
    UNIT_OK="true"
  fi
fi

# Step 4: audit-new-fixtures.sh — phase-scoped fixture-execution gate
echo "--- Step 4: scripts/audit-new-fixtures.sh"
FIXTURE_LOG="${LOG_DIR}/audit-new-fixtures.txt"
FIXTURE_REPORT="${LOG_DIR}/audit-new-fixtures-report.json"
bash scripts/audit-new-fixtures.sh --report "$FIXTURE_REPORT" > "$FIXTURE_LOG" 2>&1
FIXTURE_EXIT=$?
if [ "$FIXTURE_EXIT" -eq 0 ]; then
  FIXTURE_OK="true"
  echo "PASS fixture-execution gate"
else
  FIXTURE_OK="false"
  FAILURE_REASONS+=("fixture_gate_exit:${FIXTURE_EXIT}")
  echo "FAIL fixture-execution gate (exit $FIXTURE_EXIT)" >&2
fi

# Aggregate verdict
if [ "$UNIT_OK" = "true" ] && [ "$FIXTURE_OK" = "true" ] && [ "$PLAYWRIGHT_OK" = "true" ] && [ "$PORT_OK" = "true" ]; then
  PASSED="true"
else
  PASSED="false"
fi

echo "=== phase-smoke-gate $PHASE — done passed=$PASSED ==="
echo "Artifact: $GATE_JSON"

if [ "$PASSED" = "true" ]; then
  exit 0
else
  exit 1
fi
