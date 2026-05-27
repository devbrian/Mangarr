#!/usr/bin/env bash
# scripts/kill-orphan-test-processes.sh
#
# Shared pre-flight cleanup for the test gates (GH #252 — orphan-process leaks).
#
# Three leak classes orphan between scheduled cleanup points (fixture cancel /
# crash / hard-kill), and each blocks the NEXT run with a confusing false-failure:
#
#   1. Puppeteer/Playwright Chromium  — `ComixPlaywrightSigner`'s embedded Chromium
#      (inside the child Mangarr.Console) AND the automation-test Playwright browser
#      worker. Both are swept by `scripts/kill-orphan-chromium.ps1` which filters
#      Puppeteer/Playwright Chromium from the user's real Chrome by THREE orthogonal
#      signals (ExecutablePath not under Program Files\Google\Chrome,
#      --remote-debugging-port, --enable-automation). We invoke that script — we do
#      NOT broaden its filter, and we never `taskkill /F /IM chrome.exe`.
#
#   2. testhost.exe — `dotnet test`'s host. When killed mid-run it keeps
#      `Mangarr.Windows.dll` (and friends) file-locked → the next `dotnet build`
#      hits MSB3027 "Exceeded retry count of 10" → non-zero exit even when the run
#      itself printed "Test Run Successful." Killing leftover testhost releases the
#      lock so the rebuild succeeds.
#
#   3. Mangarr.Console.exe / Mangarr — `NzbDroneRunner.Start()`'s child process.
#      When teardown doesn't run it keeps its bound port (8989 or an ephemeral
#      port) held → the next fixture / smoke-gate Step 2 fails with "address
#      already in use" / "Process has exited".
#
# This helper is sourced (not exec'd) by phase-smoke-gate.sh and audit-new-fixtures.sh
# so the calling shell can see the function. Standalone use is also supported:
#   bash scripts/kill-orphan-test-processes.sh        # run the sweep once
#
# Windows host, PowerShell-driven process ops (Get-CimInstance / Stop-Process via
# pwsh -File), invoked from Git Bash. On non-Windows the testhost/Console kills are
# best-effort via pkill; the Chromium sweep is skipped (kill-orphan-chromium.ps1 is
# Windows-only) — the Linux CI runners don't exhibit the DLL-lock leak class.

# kill_orphan_test_processes: sweep leaked testhost + Mangarr.Console/Mangarr +
# Puppeteer/Playwright Chromium, then settle for socket/FD release. Prints what it
# killed. Always returns 0 (cleanup is best-effort; a missing process is success).
kill_orphan_test_processes() {
  local repo_root settle="${1:-2}"
  repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)

  echo "--- pre-flight cleanup: sweeping orphan test processes (GH #252)"

  if command -v pwsh >/dev/null 2>&1; then
    # Process-name kills (testhost + Mangarr.Console + Mangarr). Get-Process by
    # name, log PIDs, Stop-Process -Force. Scoped to EXACTLY these three names —
    # never a wildcard that could catch unrelated host processes.
    pwsh -NoProfile -NonInteractive -Command '
      $ErrorActionPreference = "SilentlyContinue"
      foreach ($name in @("testhost","Mangarr.Console","Mangarr")) {
        $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
        if ($procs) {
          foreach ($p in $procs) {
            Write-Host ("  killing leftover {0} PID {1}" -f $name, $p.Id)
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
          }
        }
      }
    ' || true

    # Chromium sweep — delegate to the canonical filter-by-signal script with -Kill.
    # Do NOT reimplement its filtering here (T-252: never broaden the filter).
    if [ -f "$repo_root/scripts/kill-orphan-chromium.ps1" ]; then
      pwsh -NoProfile -NonInteractive -File "$repo_root/scripts/kill-orphan-chromium.ps1" -Kill || true
    fi
  else
    # Non-Windows best-effort (Linux CI runners). Chromium DLL-lock leak class
    # does not apply; pkill the host/console process names if present.
    if command -v pkill >/dev/null 2>&1; then
      pkill -f 'testhost' 2>/dev/null && echo "  killed leftover testhost (pkill)" || true
      pkill -f 'Mangarr.Console' 2>/dev/null && echo "  killed leftover Mangarr.Console (pkill)" || true
      # Plain 'Mangarr' (anchored to a path segment / start) — the Linux backend
      # binary that NzbDroneRunner launches; would otherwise keep its port bound.
      # Mirrors the Windows branch which already kills the "Mangarr" process name.
      pkill -f '(^|/)Mangarr$' 2>/dev/null && echo "  killed leftover Mangarr (pkill)" || true
    fi
  fi

  # Settle: give the OS time to release sockets (TIME_WAIT) + file descriptors
  # before the caller rebuilds / rebinds. 2s default; callers can pass a value.
  if [ "$settle" -gt 0 ] 2>/dev/null; then
    sleep "$settle"
  fi
  echo "--- pre-flight cleanup: done"
  return 0
}

# Allow standalone invocation: `bash scripts/kill-orphan-test-processes.sh [settle]`
if [ "${BASH_SOURCE[0]}" = "${0}" ]; then
  kill_orphan_test_processes "${1:-2}"
fi
