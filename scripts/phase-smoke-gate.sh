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

# Deterministic phase dir resolution: fail on zero matches AND on ambiguity.
# CodeRabbit PR #198 finding 3255692645 — `ls | head -1` could silently write
# artifacts to the wrong dir if multiple phase folders share the same prefix
# (e.g. a phase-23 + phase-23.1 fork during a resume scenario).
shopt -s nullglob
phase_matches=(.planning/phases/"${PHASE}"-*)
shopt -u nullglob

if [ "${#phase_matches[@]}" -eq 0 ]; then
  echo "FATAL: phase dir not found for phase $PHASE (looked for .planning/phases/${PHASE}-*)" >&2
  exit 2
fi

if [ "${#phase_matches[@]}" -gt 1 ]; then
  echo "FATAL: multiple phase dirs found for phase $PHASE; refusing to guess: ${phase_matches[*]}" >&2
  exit 2
fi

PHASE_DIR="${phase_matches[0]}"
if [ ! -d "$PHASE_DIR" ]; then
  echo "FATAL: resolved phase dir is not a directory: $PHASE_DIR" >&2
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
  # Write atomically via tmp + rename so a failed write never leaves a
  # half-formed SMOKE-GATE.json that downstream verifiers might mis-parse.
  if ! cat > "${GATE_JSON}.tmp" <<EOF
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
  then
    rm -f "${GATE_JSON}.tmp"
    return 1
  fi
  mv "${GATE_JSON}.tmp" "$GATE_JSON" || return 1
  return 0
}
# Propagate write_gate failures so a missing/corrupt SMOKE-GATE.json cannot
# pass the gate. CodeRabbit PR #198 finding 3255724991 — previous trap
# `trap 'write_gate' EXIT` swallowed write_gate's exit status, so a failed
# `cat > $GATE_JSON` could still let the script exit 0 with passed=true.
trap 'rc=$?
      if ! write_gate; then
        echo "FATAL: failed to write $GATE_JSON" >&2
        rc=1
      fi
      trap - EXIT
      exit "$rc"' EXIT

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
# CodeRabbit PR #198 finding 3255692652 — portable detection across runners.
# Returns: 0 = port busy, 1 = port free, 2 = no probe tool available.
echo "--- Step 2: Port 8989 free"
is_port_8989_busy() {
  # Distinguish "probe ran cleanly, grep found nothing" (port free → return 1)
  # from "probe command itself errored" (unknown → return 2). CodeRabbit PR
  # #198 finding 3255724994 — previous direct pipe `ss | grep` treated probe
  # errors as grep-not-matching (= "port free"), enabling false-pass when ss
  # is broken or denied (e.g. some restricted containers). Capture probe
  # output into a variable, propagate probe failure as return 2.
  local probe_out
  if command -v ss >/dev/null 2>&1; then
    probe_out=$(ss -ltn 2>/dev/null) || return 2
    printf '%s\n' "$probe_out" | grep -qE '[:.]8989[[:space:]]'
    return $?
  fi
  if command -v netstat >/dev/null 2>&1; then
    # Match both `LISTEN` (Linux/BSD) and `LISTENING` (Windows) end-states.
    probe_out=$(netstat -ano 2>/dev/null) || return 2
    printf '%s\n' "$probe_out" | grep -qE '[:.]8989\b.*LISTEN(ING)?'
    return $?
  fi
  if command -v lsof >/dev/null 2>&1; then
    # lsof returns 0 = match, 1 = no match (clean run); anything else = probe error.
    lsof -nP -iTCP:8989 -sTCP:LISTEN >/dev/null 2>&1
    case $? in
      0|1) return $? ;;
      *)   return 2 ;;
    esac
  fi
  return 2
}

is_port_8989_busy
PORT_CHECK_EXIT=$?

case "$PORT_CHECK_EXIT" in
  0)
    echo "FATAL: port 8989 is occupied; smoke gate cannot bind." >&2
    echo "  Kill the existing listener (e.g. taskkill /F /PID <pid> on Windows, kill <pid> on Unix) and re-run." >&2
    PORT_OK="false"
    FAILURE_REASONS+=("port_8989_occupied")
    exit 1
    ;;
  1)
    PORT_OK="true"
    echo "PASS Port 8989 free"
    ;;
  2)
    echo "FATAL: no port-probe tool found (ss / netstat / lsof). Install one before re-running." >&2
    PORT_OK="false"
    FAILURE_REASONS+=("port_probe_unavailable")
    exit 1
    ;;
esac

# Path-PII sanitizer for committed log artifacts. CodeRabbit PR #198 finding
# 3255692643 — `dotnet test` and friends embed absolute working-tree paths
# (e.g. `C:\Users\<user>\Desktop\...`) in their stderr, which leaks the local
# user identity into git history. Replace the current repo root + common
# home-dir patterns with `<repo>` / `<user-home>` placeholders before writing
# the tracked log. Idempotent — running on already-sanitized output is a no-op.
sanitize_pii() {
  local repo_root
  repo_root=$(pwd -P 2>/dev/null || pwd)
  # Escape regex metacharacters in the resolved repo root.
  local repo_re
  repo_re=$(printf '%s' "$repo_root" | sed -e 's/[]\/$*.^|[]/\\&/g')
  # Build a unified sed that handles:
  #  - the exact resolved repo root (mixed-case Windows + Unix native)
  #  - common Windows home-dir pattern  : C:\Users\<name>\  or  C:/Users/<name>/
  #  - common Unix home-dir pattern     : /home/<name>/  or  /Users/<name>/
  sed -E \
    -e "s|${repo_re}|<repo>|gI" \
    -e 's|[Cc]:[\\/]+[Uu]sers[\\/]+[^\\/[:space:]"]+[\\/]+|<user-home>\\|g' \
    -e 's|/home/[^/[:space:]"]+/|/<user-home>/|g' \
    -e 's|/Users/[^/[:space:]"]+/|/<user-home>/|g'
}

# Step 3: Unit suite — scripts/test.sh always returns 0; parse Passed!/Failed! lines.
echo "--- Step 3: scripts/test.sh Windows Unit Test"
export TEST_DIR="./_tests/net10.0"
UNIT_LOG="${LOG_DIR}/unit-suite.txt"
bash scripts/test.sh Windows Unit Test 2>&1 | sanitize_pii > "$UNIT_LOG"
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
# Run audit-new-fixtures.sh, capture exit through PIPESTATUS, sanitize PII in log.
bash scripts/audit-new-fixtures.sh --report "$FIXTURE_REPORT" 2>&1 | sanitize_pii > "$FIXTURE_LOG"
FIXTURE_EXIT=${PIPESTATUS[0]}
# Also sanitize the report JSON since audit-new-fixtures embeds paths there too.
if [ -f "$FIXTURE_REPORT" ]; then
  sanitize_pii < "$FIXTURE_REPORT" > "$FIXTURE_REPORT.tmp" && mv "$FIXTURE_REPORT.tmp" "$FIXTURE_REPORT"
fi
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
