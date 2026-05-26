# syntax=docker/dockerfile:1.7
#
# distribution/docker/Dockerfile — Mangarr v1.0.0 runtime image (Phase 21).
#
# Multi-stage build:
#   Stage 1 (chromium-builder) — mcr.microsoft.com/playwright/dotnet:v1.59.0-noble,
#     which pre-bakes the version-matched Chromium at /ms-playwright. We COPY that
#     tree forward (no download/run needed).
#   Stage 2 (runtime)          — debian:bookworm-slim + Chromium sys-deps
#     + s6-overlay v3 + COPY of /ms-playwright + published Mangarr binary.
#
# ── Phase 17 D-01/D-02/D-03/D-04 lock — baked Chromium (Phase 33.3 driver swap) ──
# Per .planning/phases/17-comix-runtime-signer-port-puppeteersharp/17-CONTEXT.md,
# with Phase 33.3 porting the signer browser layer PuppeteerSharp -> Microsoft.Playwright
# .NET (PuppeteerSharp's CDP Runtime.enable leak is a Cloudflare challenge-platform
# bot-detection vector Playwright avoids — see .planning/phases/33.3-…/.continue-here.md).
#   D-01: single image, Chromium baked in by default (NO two-tag scheme).
#   D-02: Playwright's version-pinned Chromium (NOT distro-packaged via apt —
#         version drift per Phase 17 N-1). Sourced from the official Playwright
#         .NET image so the browser revision matches the Microsoft.Playwright
#         1.59.0 NuGet pin in Mangarr.Core.csproj exactly.
#   D-03: fixed image-layer path /ms-playwright (NOT under /config user volume;
#         survives pod-deletion / config-volume wipes). PLAYWRIGHT_BROWSERS_PATH
#         points the signer at it.
#   D-04: smoke gate at build time fails the image EARLY if the baked Chromium is
#         corrupt or sys-dep-incomplete.
#
# ── Phase 21 D-01..D-04 lock — runtime image surface ──
# Per .planning/phases/21-distribution-v1-release/21-CONTEXT.md +
#     .planning/phases/21-distribution-v1-release/21-RESEARCH.md Lock #1, #5,
#     #6, #7, #8 + Pitfall 4, #6, #7.
#   D-01: Debian + s6-overlay v3 (hotio-style user-facing convention; NEVER
#         Alpine — see Sonarr-divergence marker below + Pitfall 4).
#   D-02: lives at distribution/docker/ sibling to distribution/docker-build/.
#   D-03: Chromium at FIXED path /opt/mangarr-chromium via BrowserFetcher.
#   D-04 (layer 1): build-time `chromium --version` smoke gate in the RUNTIME
#         stage (NOT builder stage) per RESEARCH Lock #6 — catches sys-dep gaps
#         that builder-stage smoke would miss because BrowserFetcher runs in the
#         builder stage but Chromium *launches* under the runtime stage's libs.

# ─── Stage 1: chromium-builder ───
#
# Phase 33.3 (Playwright .NET port): the Chromium source is the official Playwright
# .NET image, which pre-bakes the version-matched Chromium at /ms-playwright. We pin
# the builder to the TARGET platform so /ms-playwright carries the correct-arch
# browser; COPY (below) needs no execution, so no QEMU emulation cost. Pinning to
# v1.59.0 keeps the baked browser revision in lockstep with the Microsoft.Playwright
# 1.59.0 NuGet pin in Mangarr.Core.csproj (Playwright pins browser revs per version).
# noble (Ubuntu 24.04) is glibc-linked — same C-runtime family as the
# debian:bookworm-slim runtime stage — so the COPYed Chromium runs cleanly.
#
# Sonarr divergence: arch-aware Chromium provisioning is Mangarr-only; Sonarr ships
# no headless-browser dependency.
FROM --platform=linux/$TARGETARCH mcr.microsoft.com/playwright/dotnet:v1.59.0-noble AS chromium-builder

ARG TARGETARCH

# Guard: fail early if the base image didn't bake a Chromium under /ms-playwright
# (e.g. a future Playwright image layout change). The runtime smoke gate re-checks
# under the debian libs, but this catches an empty COPY source at the source stage.
RUN test -n "$(find /ms-playwright -type f -name 'chrome' -perm -u+x | head -n1)" \
 || (echo "FATAL: playwright/dotnet:v1.59.0 image has no baked Chromium under /ms-playwright" >&2 && exit 1)

# ─── Stage 2: runtime ───
#
# (Full Mangarr-divergence rationale below.) The single-line marker on the
# immediately-preceding source line is required by the Plan 21-03 Task 1
# acceptance grep (`grep -B1 '^FROM debian' | grep '# Sonarr divergence:'`)
# AND by sonarr-consistency-audit Pattern ι allowlist coverage at phase close.
# Mangarr base is debian:bookworm-slim (glibc), NOT hotio/alpine or
# linuxserver/alpine. Playwright's baked Chromium is glibc-linked (Phase 17 N-1
# explicit forbid on musl); hotio-style refers to USER-FACING convention
# (PUID/PGID/UMASK/TZ + /config + /data + s6-overlay), not their base image.
# See Phase 21 RESEARCH Pitfall 4.
# Sonarr divergence: Debian base, NOT Alpine — Playwright glibc-pinned Chromium (Phase 17 N-1, Phase 21 RESEARCH Pitfall 4).
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
# Phase 33.3: DISPLAY=:99 points the headed Comix signer Chromium at the svc-xvfb virtual X
# display (comix.to's Cloudflare MANAGED challenge does NOT clear for a headless browser — proven
# on this Docker target). The svc-mangarr process + its Chromium child inherit DISPLAY; the
# svc-xvfb s6 longrun serves :99. See distribution/docker/root/.../svc-xvfb/run + 33.3 phase docs.
ENV DEBIAN_FRONTEND=noninteractive \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    WEBUI_PORTS=8989/tcp \
    PUID=1000 \
    PGID=1000 \
    UMASK=002 \
    TZ=Etc/UTC \
    S6_BEHAVIOUR_IF_STAGE2_FAILS=2 \
    S6_CMD_WAIT_FOR_SERVICES_MAXTIME=0 \
    DISPLAY=:99

# Chromium runtime sys-deps — 36-pkg list LIFTED VERBATIM from Phase 17
# distribution/docker-build/Dockerfile:66-74 (the explicit Phase 17 reference
# layout for this lift; see RESEARCH Lock #5). Plus 3 Mangarr-specific:
#   tzdata   — TZ env var pickup
#   libicu72 — .NET globalization-mode=Full requirement
#   libssl3  — .NET TLS stack
# Verified against pptr.dev/troubleshooting 2026-05-16 (RESEARCH §References).
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates fonts-liberation tzdata \
        libasound2 libatk-bridge2.0-0 libatk1.0-0 libc6 libcairo2 libcups2 \
        libdbus-1-3 libexpat1 libfontconfig1 libgbm1 libgcc-s1 libglib2.0-0 \
        libgtk-3-0 libnspr4 libnss3 libpango-1.0-0 libpangocairo-1.0-0 \
        libstdc++6 libx11-6 libx11-xcb1 libxcb1 libxcomposite1 libxcursor1 \
        libxdamage1 libxext6 libxfixes3 libxi6 libxrandr2 libxrender1 \
        libxtst6 lsb-release wget xdg-utils libdrm2 libxkbcommon0 \
        libicu72 libssl3 \
        xz-utils ca-certificates curl \
        # Phase 33.3: Xvfb virtual X display for the HEADED Comix signer Chromium (comix.to's
        # Cloudflare managed challenge does not clear headless). Started by the svc-xvfb s6 longrun.
        xvfb \
        # .NET runtime deps (covered by libssl3 + libicu72 above for net10.0)
        passwd \
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

# Copy pre-baked Playwright Chromium from the builder stage (Lock #1). The browser
# revision matches the Microsoft.Playwright 1.59.0 NuGet pin; PLAYWRIGHT_BROWSERS_PATH
# (set above) points the signer at it.
COPY --from=chromium-builder /ms-playwright /ms-playwright

# Copy Mangarr published binaries. Plan 21-04 docker_publish job populates
# _output/linux-${TARGETARCH}/ via actions/download-artifact@v8 BEFORE running
# docker/build-push-action@v6 (Q-7 Pattern (a)). For local dev builds the
# planner can pre-stage _output/linux-amd64/ manually or use Plan 21-05's
# scripts/docker-smoke.sh which exercises the published image.
COPY _output/linux-${TARGETARCH}/ /app/

# Copy the frontend SPA bundle. The CI `frontend` job builds via `yarn build`
# into `_output/UI/` and uploads as the `build_ui` artifact (separate from the
# per-arch backend artifacts). deploy.yml's docker_publish job downloads it
# alongside the backend artifact BEFORE invoking docker/build-push-action@v6.
# Mangarr serves the SPA from `{StartUpFolder}/{UiFolder}/index.html`; with
# StartUpFolder=/app and the default UiFolder=UI, that resolves to /app/UI/.
# Without this COPY, /api/v5 endpoints return 200 but `/` returns 404 because
# the SPA isn't on disk — surfaced during local end-to-end smoke of Phase 21.
COPY _output/UI/ /app/UI/

# Copy the s6-overlay v3 service-script tree (Task 2 + Task 3 of this plan).
COPY distribution/docker/root/ /

# Build-time smoke gate (RESEARCH Lock #6 — RUNTIME STAGE, not builder stage —
# catches sys-dep gaps the builder stage would miss because the browser launches
# under the runtime stage's libs). Playwright bakes Chromium to a versioned subdir
# like /ms-playwright/chromium-<rev>/chrome-linux/chrome ; find-based discovery
# avoids hardcoding the revision.
RUN CHROME_BIN="$(find /ms-playwright -type f -name 'chrome' -perm -u+x | head -n1)" \
 && [ -n "$CHROME_BIN" ] || (echo "FATAL: no Playwright chrome exe under /ms-playwright" >&2 && exit 1) \
 && "$CHROME_BIN" --version \
 || (echo "FATAL: Chromium binary corrupted or missing sys-deps" >&2 && exit 1)

# Volume contract (Lock #8): /config = Mangarr config + DB; /data = library root.
VOLUME ["/config", "/data"]

# WebUI port (matches Sonarr inheritance per Lock #8 + PROJECT.md port lock).
EXPOSE 8989

# s6-overlay v3 boot (Lock #7); /init is provided by the s6-overlay-noarch
# tarball extracted above. svc-mangarr longrun + init-mangarr-config oneshot
# are wired via the s6-rc.d/ tree copied above.
ENTRYPOINT ["/init"]
