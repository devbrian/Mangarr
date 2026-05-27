#!/usr/bin/env bash
# scripts/validate-no-orphan-leaks.sh
#
# GH #252 — orphan-process leak validation harness.
#
# Runs `scripts/phase-smoke-gate.sh <phase>` back-to-back N times (default 5)
# with NO manual cleanup between runs, and asserts that the process population
# of the three leak classes returns to its pre-run baseline within a settle
# window after EACH iteration:
#
#   1. testhost.exe                       (DLL-lock → MSB3027 false-failure)
#   2. Mangarr.Console.exe / Mangarr      (port-bind → address-already-in-use)
#   3. Puppeteer/Playwright Chromium      (classified by kill-orphan-chromium.ps1)
#
# If any class fails to return to baseline within the settle window, the script
# prints the leaked PIDs + command lines and exits non-zero. This is the
# acceptance harness for issue #252 criterion 1 ("runs cleanly N times
# back-to-back, zero false-flakes") and criterion 2 ("passes against HEAD").
#
# The phase-smoke-gate's own Step 0 pre-flight cleanup is what makes back-to-back
# runs self-heal; this harness PROVES that property holds rather than relying on
# the operator to remember to clean up.
#
# Usage:
#   bash scripts/validate-no-orphan-leaks.sh <phase-number> [iterations] [settle-seconds]
#
# Args:
#   <phase-number>   required — the phase whose smoke gate to exercise (e.g. 35)
#   [iterations]     optional — number of back-to-back runs (default 5)
#   [settle-seconds] optional — seconds to wait for return-to-baseline (default 30)
#
# Exit codes:
#   0  — all iterations ran and every leak class returned to baseline each time
#   1  — a leak was detected (PIDs + cmdlines printed) OR a smoke gate run errored
#        in a way that left orphans
#   2  — usage error
#
# Windows host, PowerShell-driven process snapshots (Get-CimInstance) via pwsh.
# On non-Windows the snapshot uses `pgrep`/`ps` best-effort (CI Linux runners do
# not exhibit the DLL-lock leak class, but the Mangarr.Console port-bind class
# is still meaningful there).

set -uo pipefail

PHASE="${1:-}"
ITERATIONS="${2:-5}"
SETTLE_SECONDS="${3:-30}"

if [ -z "$PHASE" ]; then
  echo "Usage: $0 <phase-number> [iterations] [settle-seconds]" >&2
  exit 2
fi
if ! [[ "$ITERATIONS" =~ ^[0-9]+$ ]] || [ "$ITERATIONS" -lt 1 ]; then
  echo "FATAL: iterations must be a positive integer (got '$ITERATIONS')" >&2
  exit 2
fi
if ! [[ "$SETTLE_SECONDS" =~ ^[0-9]+$ ]]; then
  echo "FATAL: settle-seconds must be a non-negative integer (got '$SETTLE_SECONDS')" >&2
  exit 2
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

# snapshot_counts: print three integers "testhost console chromium" for the
# current process population of the three leak classes. Chromium is counted via
# the SAME three-signal filter kill-orphan-chromium.ps1 uses (so user Chrome is
# never counted). Prints PID+cmdline detail to stderr when $1 == "verbose".
snapshot_counts() {
  local verbose="${1:-}"
  if command -v pwsh >/dev/null 2>&1; then
    pwsh -NoProfile -NonInteractive -Command "
      \$ErrorActionPreference = 'SilentlyContinue'
      \$verbose = '$verbose' -eq 'verbose'

      \$testhost = @(Get-Process -Name testhost -ErrorAction SilentlyContinue)
      \$console  = @(Get-Process -Name Mangarr.Console,Mangarr -ErrorAction SilentlyContinue)

      # Chromium: replicate kill-orphan-chromium.ps1's three-signal filter.
      \$chromes = @(Get-CimInstance Win32_Process -Filter \"Name = 'chrome.exe'\")
      \$leaked = @()
      foreach (\$p in \$chromes) {
        \$cmd = if (\$p.CommandLine) { \$p.CommandLine } else { '' }
        \$exe = if (\$p.ExecutablePath) { \$p.ExecutablePath } else { '' }
        \$isUser = \$exe -match '\\\\Program Files( \\(x86\\))?\\\\Google\\\\Chrome\\\\Application\\\\chrome\\.exe\$'
        \$dbg = \$cmd -match '--remote-debugging-port='
        \$auto = \$cmd -match '--enable-automation'
        if ((-not \$isUser) -or \$dbg -or \$auto) { \$leaked += \$p }
      }

      if (\$verbose) {
        foreach (\$p in \$testhost) { [Console]::Error.WriteLine(\"  testhost PID \$(\$p.Id)\") }
        foreach (\$p in \$console)  { [Console]::Error.WriteLine(\"  console  PID \$(\$p.Id) (\$(\$p.ProcessName))\") }
        foreach (\$p in \$leaked)   { [Console]::Error.WriteLine(\"  chromium PID \$(\$p.ProcessId) cmd=\$(\$p.CommandLine)\") }
      }

      Write-Output (\"{0} {1} {2}\" -f \$testhost.Count, \$console.Count, \$leaked.Count)
    "
  else
    # Non-Windows best-effort: pgrep by name pattern. Chromium DLL-lock leak class
    # not applicable; count headless/automation chrome via cmdline flags.
    local th con chr
    th=$(pgrep -c -f 'testhost' 2>/dev/null || echo 0)
    con=$(pgrep -c -f 'Mangarr.Console|/Mangarr$' 2>/dev/null || echo 0)
    chr=$(pgrep -c -f 'chrome.*--(remote-debugging-port|enable-automation)' 2>/dev/null || echo 0)
    if [ "$verbose" = "verbose" ]; then
      pgrep -a -f 'testhost' 2>/dev/null | sed 's/^/  testhost /' >&2 || true
      pgrep -a -f 'Mangarr.Console|/Mangarr$' 2>/dev/null | sed 's/^/  console  /' >&2 || true
      pgrep -a -f 'chrome.*--(remote-debugging-port|enable-automation)' 2>/dev/null | sed 's/^/  chromium /' >&2 || true
    fi
    echo "$th $con $chr"
  fi
}

echo "=== validate-no-orphan-leaks: phase=$PHASE iterations=$ITERATIONS settle=${SETTLE_SECONDS}s ==="
echo ""

# Baseline BEFORE any runs (after a clean sweep so a pre-existing leak from an
# earlier unrelated session doesn't get blamed on this validation).
echo "--- Establishing clean baseline (pre-sweep)"
bash "$REPO_ROOT/scripts/kill-orphan-test-processes.sh" 2 || true
read -r BASE_TH BASE_CON BASE_CHR <<< "$(snapshot_counts)"
echo "Baseline: testhost=$BASE_TH console=$BASE_CON chromium=$BASE_CHR"
echo ""

OVERALL_RC=0

for i in $(seq 1 "$ITERATIONS"); do
  echo "=================================================================="
  echo "=== Iteration $i / $ITERATIONS — running phase-smoke-gate $PHASE"
  echo "=================================================================="
  GATE_RC=0
  bash "$REPO_ROOT/scripts/phase-smoke-gate.sh" "$PHASE" || GATE_RC=$?
  echo "--- Iteration $i: phase-smoke-gate exit=$GATE_RC"

  # A non-zero gate exit is NOT itself a leak — the gate may legitimately FAIL
  # (e.g. a real unit-test failure). What this harness validates is that AFTER
  # the gate finishes (pass OR fail), the process population returns to baseline.
  # We record the gate RC but only FAIL the validation on a LEAK.

  echo "--- Iteration $i: waiting up to ${SETTLE_SECONDS}s for return-to-baseline"
  LEAK_FOUND=1
  waited=0
  while [ "$waited" -le "$SETTLE_SECONDS" ]; do
    read -r TH CON CHR <<< "$(snapshot_counts)"
    if [ "$TH" -le "$BASE_TH" ] && [ "$CON" -le "$BASE_CON" ] && [ "$CHR" -le "$BASE_CHR" ]; then
      LEAK_FOUND=0
      echo "    returned to baseline after ${waited}s (testhost=$TH console=$CON chromium=$CHR)"
      break
    fi
    sleep 2
    waited=$((waited + 2))
  done

  if [ "$LEAK_FOUND" -ne 0 ]; then
    read -r TH CON CHR <<< "$(snapshot_counts)"
    echo "" >&2
    echo "LEAK DETECTED after iteration $i (settle window ${SETTLE_SECONDS}s expired):" >&2
    echo "  baseline: testhost=$BASE_TH console=$BASE_CON chromium=$BASE_CHR" >&2
    echo "  current:  testhost=$TH console=$CON chromium=$CHR" >&2
    echo "  leaked process detail:" >&2
    snapshot_counts verbose >/dev/null   # detail goes to stderr
    OVERALL_RC=1
    # Clean up so the NEXT iteration (if any) starts fair; the leak is already
    # recorded against this iteration.
    bash "$REPO_ROOT/scripts/kill-orphan-test-processes.sh" 2 || true
  fi
  echo ""
done

echo "=================================================================="
if [ "$OVERALL_RC" -eq 0 ]; then
  echo "PASS: $ITERATIONS iteration(s) completed; every leak class returned to baseline each time."
else
  echo "FAIL: at least one iteration leaked a process class beyond baseline (see detail above)."
fi
echo "=================================================================="
exit "$OVERALL_RC"
