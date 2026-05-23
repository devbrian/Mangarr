#!/usr/bin/env bash
# Stop the local Mangarr Console build.
#
# Strategy:
#   1. POST /api/v5/system/shutdown with the API key from config.xml (graceful).
#   2. Fall back to taskkill/kill on the PID recorded by start-mangarr.sh.
#   3. Wait until port 8989 is free.
#
# Flags:
#   -p, --port PORT    Override the port (default 8989, read from config.xml).
#   -t, --timeout SEC  Wait up to N seconds for the port to free (default 15).
#   -f, --force        Skip the graceful shutdown attempt; kill immediately.
#   -h, --help         Print usage.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$REPO_ROOT/_output/.mangarr-run"
PID_FILE="$RUN_DIR/mangarr.pid"

PORT=""
TIMEOUT=15
FORCE=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        -p|--port) PORT="$2"; shift 2 ;;
        -t|--timeout) TIMEOUT="$2"; shift 2 ;;
        -f|--force) FORCE=1; shift ;;
        -h|--help)
            awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) echo "unknown flag: $1" >&2; exit 2 ;;
    esac
done

# Locate config.xml for port + API key.
CONFIG_XML=""
for CFG in "/c/ProgramData/Mangarr/config.xml" "$HOME/.config/Mangarr/config.xml"; do
    if [[ -f "$CFG" ]]; then
        CONFIG_XML="$CFG"
        break
    fi
done

if [[ -z "$PORT" && -n "$CONFIG_XML" ]]; then
    CFG_PORT=$(grep -oE "<Port>[0-9]+</Port>" "$CONFIG_XML" | grep -oE "[0-9]+" || true)
    PORT="${CFG_PORT:-}"
fi
PORT="${PORT:-8989}"

API_KEY=""
if [[ -n "$CONFIG_XML" ]]; then
    API_KEY=$(grep -oE "<ApiKey>[^<]+</ApiKey>" "$CONFIG_XML" | sed 's/<\/\?ApiKey>//g' || true)
fi

MANGARR_UP() {
    # Primary check: does Mangarr's /login respond 200? This is the same probe
    # start-mangarr.sh uses for readiness, and it sidesteps the netstat-column-
    # parsing fragility (Git Bash on Windows quirks, varying widths, IPv4/v6
    # representation differences). If the HTTP probe fails AND we're confident
    # the port isn't bound to something else, we treat Mangarr as already down.
    local code
    code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 6 "http://localhost:$PORT/login" 2>/dev/null || echo "000")
    [[ "$code" =~ ^(200|3[0-9][0-9])$ ]]
}

if ! MANGARR_UP; then
    echo "[stop-mangarr] Mangarr is not responding on http://localhost:$PORT — nothing to stop."
    rm -f "$PID_FILE"
    exit 0
fi

# Step 1: graceful shutdown via API.
if [[ "$FORCE" -eq 0 && -n "$API_KEY" ]]; then
    echo "[stop-mangarr] Sending POST /api/v5/system/shutdown"
    if curl -s -o /dev/null -w '' -X POST \
        "http://localhost:$PORT/api/v5/system/shutdown" \
        -H "X-Api-Key: $API_KEY" 2>/dev/null; then
        echo "[stop-mangarr] Shutdown request accepted; waiting up to ${TIMEOUT}s for Mangarr to stop."
        DEADLINE=$(( $(date +%s) + TIMEOUT ))
        while [[ $(date +%s) -lt $DEADLINE ]]; do
            if ! MANGARR_UP; then
                echo "[stop-mangarr] Stopped cleanly."
                rm -f "$PID_FILE"
                exit 0
            fi
            sleep 1
        done
        echo "[stop-mangarr] Graceful shutdown timed out after ${TIMEOUT}s; falling back to kill." >&2
    else
        echo "[stop-mangarr] Could not reach API; falling back to kill." >&2
    fi
fi

# Step 2: kill the recorded PID, verifying the process actually exists and
# matches Mangarr.Console.exe (Git Bash's `kill -0` returns success for stale
# wrapper PIDs under Windows, so we cross-check by image name when tasklist is
# available). Falls through to the image-name kill below if the recorded PID
# can't be verified or the kill doesn't take the port down.
EXE_BASENAME="Mangarr.Console.exe"
pid_is_mangarr() {
    local pid="$1"
    [[ -z "$pid" ]] && return 1
    if command -v tasklist >/dev/null 2>&1; then
        tasklist //NH //FI "PID eq $pid" 2>/dev/null | grep -q "$EXE_BASENAME"
    else
        kill -0 "$pid" 2>/dev/null
    fi
}

KILLED=0
if [[ -f "$PID_FILE" ]]; then
    PID=$(cat "$PID_FILE" || true)
    if pid_is_mangarr "$PID"; then
        echo "[stop-mangarr] Killing recorded pid $PID"
        if command -v taskkill >/dev/null 2>&1; then
            taskkill //F //PID "$PID" >/dev/null 2>&1 || true
        else
            kill -TERM "$PID" 2>/dev/null || true
            sleep 2
            kill -KILL "$PID" 2>/dev/null || true
        fi
        KILLED=1
    elif [[ -n "$PID" ]]; then
        echo "[stop-mangarr] Recorded pid $PID does not match $EXE_BASENAME (likely a stale wrapper); falling through to image-name kill."
    fi
fi

# Image-name fallback. Runs when: (a) no PID file, (b) recorded PID stale, OR
# (c) PID-based kill didn't actually take Mangarr down (e.g. taskkill silently
# no-op'd because Windows considered the PID exited but the .exe was a child).
if [[ "$KILLED" -eq 0 ]] || MANGARR_UP; then
    if command -v taskkill >/dev/null 2>&1; then
        echo "[stop-mangarr] taskkill /F /IM $EXE_BASENAME"
        taskkill //F //IM "$EXE_BASENAME" >/dev/null 2>&1 || true
    elif command -v pkill >/dev/null 2>&1; then
        echo "[stop-mangarr] pkill -f Mangarr.Console"
        pkill -f Mangarr.Console || true
    fi
fi

# Step 3: confirm.
DEADLINE=$(( $(date +%s) + TIMEOUT ))
while [[ $(date +%s) -lt $DEADLINE ]]; do
    if ! MANGARR_UP; then
        echo "[stop-mangarr] Stopped."
        rm -f "$PID_FILE"
        exit 0
    fi
    sleep 1
done

echo "[stop-mangarr] Mangarr is still responding on port $PORT after ${TIMEOUT}s." >&2
exit 1
