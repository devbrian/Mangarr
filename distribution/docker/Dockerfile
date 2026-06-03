# syntax=docker/dockerfile:1.7
#
# distribution/docker/Dockerfile — Mangarr runtime image.
#
# Single-stage build:
#   debian:bookworm-slim + .NET runtime sys-deps + s6-overlay v3 + the published
#   self-contained Mangarr binary + the frontend SPA bundle.
#
# ── Phase 39 (#313) — browser-free image ──
# Phase 39 retired the only embedded-browser consumer in Mangarr (the Comix runtime
# signer, ComixPlaywrightSigner) and dropped Microsoft.Playwright from Mangarr.Core;
# Mangarr now drives only the external manga gateway (GatewayIndexer +
# GatewayDownloadClient) and ships ZERO embedded browser. Issue #313 removed the
# image-layer tail this left behind: the chromium-builder stage, the baked
# /ms-playwright Chromium, the ~36-pkg Chromium runtime sys-dep list, the build-time
# `chrome --version` smoke gate, the PLAYWRIGHT_BROWSERS_PATH + DISPLAY env, and the
# svc-xvfb s6 longrun (removed from distribution/docker/root/ as a 3-file set).
#
# ── Phase 21 D-01/D-02/D-08 lock — runtime image surface ──
# Per .planning/phases/21-distribution-v1-release/21-CONTEXT.md +
#     .planning/phases/21-distribution-v1-release/21-RESEARCH.md Lock #1, #7, #8.
#   D-01: Debian + s6-overlay v3 (hotio-style user-facing convention; NEVER
#         Alpine — see Sonarr-divergence marker below + Pitfall 4).
#   D-02: lives at distribution/docker/ sibling to distribution/docker-build/.

# ─── runtime stage ───
#
# (Full Mangarr-divergence rationale below.) The single-line marker on the
# immediately-preceding source line is required by the Plan 21-03 Task 1
# acceptance grep (`grep -B1 '^FROM debian' | grep '# Sonarr divergence:'`)
# AND by sonarr-consistency-audit Pattern ι allowlist coverage at phase close.
# Mangarr base is debian:bookworm-slim (glibc), NOT hotio/alpine or
# linuxserver/alpine. hotio-style refers to USER-FACING convention
# (PUID/PGID/UMASK/TZ + /config + /data + s6-overlay), not their base image.
# See Phase 21 RESEARCH Pitfall 4.
# Sonarr divergence: first-party runtime image with hotio-style /config + /data + s6-overlay v3 convention (Sonarr ships no first-party arr-family runtime image of this shape).
FROM debian:bookworm-slim

ARG TARGETARCH
ARG MANGARR_VERSION="dev"
ARG S6_OVERLAY_VERSION=3.2.0.2

# OCI labels — Lock #3 enables GHCR auto-link to the repo + permissions
# inheritance (https://github.com/devbrian/Mangarr).
LABEL org.opencontainers.image.source="https://github.com/devbrian/Mangarr" \
      org.opencontainers.image.version="${MANGARR_VERSION}" \
      org.opencontainers.image.title="Mangarr" \
      org.opencontainers.image.description="Manga/manhwa/manhua library manager and downloader (Sonarr fork)." \
      org.opencontainers.image.licenses="GPL-3.0-only"

# Hotio-style user-facing convention (Lock #8) + s6-overlay v3 boot tuning.
ENV DEBIAN_FRONTEND=noninteractive \
    WEBUI_PORTS=8989/tcp \
    PUID=1000 \
    PGID=1000 \
    UMASK=002 \
    TZ=Etc/UTC \
    S6_BEHAVIOUR_IF_STAGE2_FAILS=2 \
    S6_CMD_WAIT_FOR_SERVICES_MAXTIME=0

# .NET runtime sys-deps only — Mangarr ships zero embedded browser since Phase 39
# (#313), so the former ~36-pkg Chromium sys-dep list + xvfb are gone.
#   ca-certificates — TLS root store
#   tzdata          — TZ env var pickup
#   libicu72        — .NET globalization-mode=Full requirement
#   libssl3         — .NET TLS stack
#   curl            — health-poll / smoke tooling
#   xz-utils        — s6-overlay tarball extraction
#   passwd          — abc user creation (s6-setuidgid / PUID-PGID convention)
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates tzdata libicu72 libssl3 curl xz-utils passwd \
 && rm -rf /var/lib/apt/lists/*

# s6-overlay v3 install (RESEARCH Lock #7 + Pitfall 6 arch translation).
# Pitfall 6: TARGETARCH (amd64/arm64) must translate to S6_ARCH (x86_64/aarch64)
# before the ADD; naive ${TARGETARCH} would 404 on arm64.
RUN set -eux; \
    case "${TARGETARCH}" in \
      amd64) S6_ARCH=x86_64 ;; \
      arm64) S6_ARCH=aarch64 ;; \
      *) echo "FATAL: unsupported TARGETARCH=${TARGETARCH} (expected amd64 or arm64)" >&2; exit 1 ;; \
    esac; \
    cd /tmp; \
    curl -fsSL -o s6-overlay-noarch.tar.xz \
        "https://github.com/just-containers/s6-overlay/releases/download/v${S6_OVERLAY_VERSION}/s6-overlay-noarch.tar.xz"; \
    curl -fsSL -o "s6-overlay-${S6_ARCH}.tar.xz" \
        "https://github.com/just-containers/s6-overlay/releases/download/v${S6_OVERLAY_VERSION}/s6-overlay-${S6_ARCH}.tar.xz"; \
    tar -C / -Jxpf s6-overlay-noarch.tar.xz; \
    tar -C / -Jxpf "s6-overlay-${S6_ARCH}.tar.xz"; \
    rm -f s6-overlay-noarch.tar.xz "s6-overlay-${S6_ARCH}.tar.xz"

# Copy Mangarr published binaries. Plan 21-04 docker_publish job populates
# _output/linux-${TARGETARCH}/ via actions/download-artifact@v8 BEFORE running
# docker/build-push-action@v6 (Q-7 Pattern (a)). For local dev builds the
# planner can pre-stage _output/linux-amd64/ manually or use Plan 21-05's
# scripts/docker-smoke.sh which exercises the published image.
COPY _output/linux-${TARGETARCH}/ /app/

# Ensure the entrypoint binaries carry the POSIX exec bit even when the build
# context originated on a filesystem that cannot set it (e.g. a Windows dev host
# building against a remote Linux daemon via `docker context`). Idempotent with
# the CI deploy.yml "Flatten linux-x64 artifact" chmod — a no-op on the CI path
# (already +x); on a Windows-origin local build it is what stops
# `s6-applyuidgid: fatal: unable to exec /app/Mangarr: No such file or directory`
# (POSIX returns ENOENT when exec'ing a file that exists but lacks +x).
RUN chmod +x /app/Mangarr /app/createdump 2>/dev/null || true; \
    [ -f /app/Mangarr.Update/Mangarr.Update ] && chmod +x /app/Mangarr.Update/Mangarr.Update || true

# Copy the frontend SPA bundle. The CI `frontend` job builds via `yarn build`
# into `_output/UI/` and uploads as the `build_ui` artifact (separate from the
# per-arch backend artifacts). deploy.yml's docker_publish job downloads it
# alongside the backend artifact BEFORE invoking docker/build-push-action@v6.
# Mangarr serves the SPA from `{StartUpFolder}/{UiFolder}/index.html`; with
# StartUpFolder=/app and the default UiFolder=UI, that resolves to /app/UI/.
# Without this COPY, /api/v5 endpoints return 200 but `/` returns 404 because
# the SPA isn't on disk — surfaced during local end-to-end smoke of Phase 21.
COPY _output/UI/ /app/UI/

# Copy the s6-overlay v3 service-script tree (init-mangarr-config oneshot +
# svc-mangarr longrun).
COPY distribution/docker/root/ /

# Volume contract (Lock #8): /config = Mangarr config + DB; /data = library root.
VOLUME ["/config", "/data"]

# WebUI port (matches Sonarr inheritance per Lock #8 + PROJECT.md port lock).
EXPOSE 8989

# s6-overlay v3 boot (Lock #7); /init is provided by the s6-overlay-noarch
# tarball extracted above. svc-mangarr longrun + init-mangarr-config oneshot
# are wired via the s6-rc.d/ tree copied above.
ENTRYPOINT ["/init"]
