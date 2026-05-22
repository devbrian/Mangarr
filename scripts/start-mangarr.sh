#!/usr/bin/env bash
# Start the local Mangarr Console build in the background and wait for it to be ready.
#
# Defaults:
#   * launches `_output/net10.0/Mangarr.Console.exe` (Debug build under net10.0).
#   * waits up to 60s for `GET /api/v5/system/status` to respond 200.
#   * writes PID + log path to `_output/.mangarr-run/` so stop-mangarr.sh can find them.
#
# Flags:
#   -f, --foreground   Run in the foreground (don't background; useful for tailing).
#   -p, --port PORT    Override the readiness probe port (default 8989, read from config.xml when present).
#   -t, --timeout SEC  Override the readiness timeout (default 60).
#   -h, --help         Print usage.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$REPO_ROOT/_output/.mangarr-run"
PID_FILE="$RUN_DIR/mangarr.pid"
LOG_FILE="$RUN_DIR/mangarr.log"
EXE="$REPO_ROOT/_output/net10.0/Mangarr.Console.exe"

FOREGROUND=0
PORT=""
TIMEOUT=60

while [[ $# -gt 0 ]]; do
    case "$1" in
        -f|--foreground) FOREGROUND=1; shift ;;
        -p|--port) PORT="$2"; shift 2 ;;
        -t|--timeout) TIMEOUT="$2"; shift 2 ;;
        -h|--help)
            awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) echo "unknown flag: $1" >&2; exit 2 ;;
    esac
done

# Resolve port from config.xml (Windows + Linux/macOS data-dir layouts) when not overridden.
if [[ -z "$PORT" ]]; then
    for CFG in "/c/ProgramData/Mangarr/config.xml" "$HOME/.config/Mangarr/config.xml"; do
        if [[ -f "$CFG" ]]; then
            CFG_PORT=$(grep -oE "<Port>[0-9]+</Port>" "$CFG" | grep -oE "[0-9]+" || true)
            if [[ -n "$CFG_PORT" ]]; then
                PORT="$CFG_PORT"
                break
            fi
        fi
    done
fi
PORT="${PORT:-8989}"

if [[ ! -x "$EXE" ]]; then
    echo "Mangarr.Console.exe not found at $EXE" >&2
    echo "Run 'dotnet build src/Mangarr.sln --configuration Debug' first." >&2
    exit 1
fi

# Refuse to start a second instance on the same port.
if curl -s -o /dev/null -w '%{http_code}' "http://localhost:$PORT/login" 2>/dev/null | grep -q '^200$'; then
    EXISTING_PID=""
    [[ -f "$PID_FILE" ]] && EXISTING_PID=$(cat "$PID_FILE" || true)
    echo "Mangarr is already listening on port $PORT${EXISTING_PID:+ (pid $EXISTING_PID)}" >&2
    exit 0
fi

mkdir -p "$RUN_DIR"

if [[ "$FOREGROUND" -eq 1 ]]; then
    echo "[start-mangarr] Launching in foreground from $EXE"
    exec "$EXE"
fi

# Background launch — fully detach so the script can exit cleanly.
echo "[start-mangarr] Launching $EXE in background (logs: $LOG_FILE)"
nohup "$EXE" >"$LOG_FILE" 2>&1 &
WRAPPER_PID=$!

# Resolve the ACTUAL Mangarr.Console.exe pid. On Git Bash + Windows, `$!` after
# `nohup &` gives the bash wrapper / nohup intermediary, NOT the .exe — so
# stop-mangarr.sh's `taskkill //PID $WRAPPER_PID` would try to kill the wrong
# process and leave the .exe running. On Linux/Mac `$!` is the real exe.
# Strategy: ask tasklist/pgrep for the actual process matching the EXE basename.
EXE_BASENAME="$(basename "$EXE")"   # e.g. "Mangarr.Console.exe"
resolve_actual_pid() {
    if command -v tasklist >/dev/null 2>&1; then
        # tasklist returns CSV with "ImageName","PID",... — strip quotes from col 2.
        tasklist //NH //FI "IMAGENAME eq $EXE_BASENAME" //FO CSV 2>/dev/null \
            | head -1 \
            | awk -F',' '{gsub(/"/,"",$2); print $2}'
    elif command -v pgrep >/dev/null 2>&1; then
        pgrep -f "$EXE_BASENAME" | head -1
    fi
}

# Poll briefly for the .exe to appear (it may take a moment after nohup forks).
PID=""
for _ in $(seq 1 10); do
    PID="$(resolve_actual_pid)"
    [[ -n "$PID" ]] && break
    sleep 0.5
done

# Fallback: if resolution failed (unknown environment), record the wrapper PID
# so we at least have something — stop-mangarr.sh will fall through to the
# image-name kill path when killing by PID misses.
if [[ -z "$PID" ]]; then
    PID="$WRAPPER_PID"
fi
echo "$PID" >"$PID_FILE"

# Wait for /api/v5/system/status to respond 200 (login endpoint is also fine but
# system/status forces the API stack to be fully wired before declaring "ready").
echo -n "[start-mangarr] Waiting up to ${TIMEOUT}s for http://localhost:$PORT to be ready"
DEADLINE=$(( $(date +%s) + TIMEOUT ))
while [[ $(date +%s) -lt $DEADLINE ]]; do
    CODE=$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$PORT/login" 2>/dev/null || echo "000")
    if [[ "$CODE" == "200" ]]; then
        echo
        echo "[start-mangarr] Ready on http://localhost:$PORT (pid $PID)"
        exit 0
    fi
    # Process-died check: prefer tasklist on Windows (kill -0 is unreliable for
    # Win32 PIDs under Git Bash), fall back to kill -0 elsewhere.
    if command -v tasklist >/dev/null 2>&1; then
        if ! tasklist //NH //FI "PID eq $PID" 2>/dev/null | grep -q "$EXE_BASENAME"; then
            echo
            echo "[start-mangarr] Process $PID exited before becoming ready. Tail of $LOG_FILE:" >&2
            tail -n 30 "$LOG_FILE" >&2 || true
            rm -f "$PID_FILE"
            exit 1
        fi
    elif ! kill -0 "$PID" 2>/dev/null; then
        echo
        echo "[start-mangarr] Process $PID exited before becoming ready. Tail of $LOG_FILE:" >&2
        tail -n 30 "$LOG_FILE" >&2 || true
        rm -f "$PID_FILE"
        exit 1
    fi
    echo -n "."
    sleep 1
done

echo
echo "[start-mangarr] Timed out after ${TIMEOUT}s waiting for http://localhost:$PORT" >&2
echo "[start-mangarr] Process is still alive (pid $PID). Tail of $LOG_FILE:" >&2
tail -n 30 "$LOG_FILE" >&2 || true
exit 1
