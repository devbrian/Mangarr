#!/usr/bin/env bash
# scripts/docker-smoke.sh — Phase 21 D-04 layer 2 per-arch boot-smoke gate.
#
# Purpose : `docker run` the runtime image, poll /ping (the AllowAnonymous
#           Sonarr-inherited health endpoint at Mangarr.Http/Ping/PingController.cs)
#           until HTTP 200, defensive-shape-check the body for the `OK` token,
#           then SIGTERM the container with grace. Catches startup regressions
#           (missing native deps, broken DI bootstrap, port-binding failures)
#           BEFORE the image gets pushed to GHCR. Earlier draft polled
#           /api/v5/system/status, but that's auth-protected; on a freshly-
#           created /config the ApiKeyAuthenticationHandler challenges the
#           unauthenticated request → HTTP 401 → smoke loop never gets 200
#           (caught in v1.0.0 deploy run 25980833591 boot-smoke step).
# Usage   : scripts/docker-smoke.sh --image <ref>
#           e.g. scripts/docker-smoke.sh --image ghcr.io/devbrian/mangarr:1.0.0.42
# Invoked : (a) CI step in .github/workflows/deploy.yml (Phase 21 Plan 21-04), per arch.
#           (b) Local dev via `bash scripts/docker-smoke.sh --image <locally-built-tag>`.
#           (c) mangarr-phase-smoke-test SKILL Task 10 (Phase 21 Plan 21-05).
#
# Sonarr divergence: no Sonarr peer script — Mangarr is the first arr-family fork to
# ship a first-party runtime Docker image (Phase 21 D-01 lock Debian + s6-overlay v3;
# Phase 21 RESEARCH Pitfall 4). The image is browser-free since Phase 39 / #313.
# Pattern S2 marker per Phase 15 D-09.

set -euo pipefail

IMAGE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --image) IMAGE="$2"; shift 2 ;;
    *) echo "Unknown arg: $1" >&2; exit 2 ;;
  esac
done
[[ -z "$IMAGE" ]] && { echo "Usage: $0 --image <ref>" >&2; exit 2; }

# PORT chosen high to avoid collision with a running dev instance on the canonical 8989.
CONTAINER="mangarr-smoke-$$"
PORT=18989

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo "[smoke] Starting $IMAGE as $CONTAINER on port $PORT..."
docker run -d --name "$CONTAINER" -p "${PORT}:8989" "$IMAGE"

echo "[smoke] Waiting for /ping (bounded — 30 attempts x 2s = 60s ceiling)..."
# Bounded for-loop per CLAUDE.md user-memory feedback_bash_until_loop_pitfall.md.
# NEVER unbounded poller loops — they orphan to PID 1 if the test never satisfies.
OK=0
for i in $(seq 1 30); do
  if curl -fsS "http://localhost:${PORT}/ping" >/dev/null 2>&1; then
    echo "[smoke] HTTP 200 received at attempt $i"
    OK=1
    break
  fi
  sleep 2
done

if [[ "$OK" -ne 1 ]]; then
  echo "[smoke] FATAL: backend did not respond within 60s" >&2
  docker logs "$CONTAINER" | tail -50 >&2
  exit 1
fi

# Defensive shape check — Sonarr's PingController.Get returns a JSON object
# `{"status":"OK"}` on a successful boot. A 200 with the wrong shape would
# mean a reverse proxy / middleware answered instead of the Mangarr backend.
BODY=$(curl -fsS "http://localhost:${PORT}/ping")
if ! echo "$BODY" | grep -q '"OK"'; then
  echo "[smoke] FATAL: /ping response shape missing 'OK' token" >&2
  echo "$BODY" >&2
  exit 1
fi

# Clean shutdown signal per D-04 — SIGTERM with 15s grace before forced kill.
echo "[smoke] Shutting down $CONTAINER (SIGTERM, 15s grace)..."
docker stop "$CONTAINER" --time 15 >/dev/null

echo "[smoke] OK — $IMAGE booted, served API, shut down cleanly."
