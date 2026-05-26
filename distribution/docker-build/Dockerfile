FROM ubuntu:focal AS builder

ENV DEBIAN_FRONTEND noninteractive 
ENV MONO_VERSION 5.18

RUN apt-get update && \
    apt-get -y -o Dpkg::Options::="--force-confold" install --no-install-recommends \
        apt-transport-https \
        wget dirmngr gpg gpg-agent \
        # add-apt-repository for PPAs
        software-properties-common && \
    rm -rf /var/lib/apt/lists/*
	
RUN apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF && \
    echo "deb http://download.mono-project.com/repo/debian stable-focal main" > /etc/apt/sources.list.d/mono-official-stable.list && \
    apt-get update && apt-get install -y \ 
        devscripts build-essential tofrodos \
        dh-make dh-systemd \
        cli-common-dev \
        mono-complete \
        sqlite3 libcurl4 mediainfo
RUN apt-get upgrade -y

RUN apt-cache policy mono-complete
RUN apt-cache policy cli-common-dev

# ── Phase 17 D-02/D-03/D-04 — Chromium runtime sys-deps + BrowserFetcher pre-download ──
# Per .planning/phases/17-comix-runtime-signer-port-puppeteersharp/17-CONTEXT.md.
# D-01: single image, Chromium baked in by default (NO two-tag scheme).
# D-02: BrowserFetcher pre-download (NOT distro `apt-get install chromium` — version drift).
# D-03: fixed path /opt/mangarr-chromium (NOT under /config user-mounted volume).
# D-04: extend existing base image — minimal divergence.
# Pitfall 1: build-time `chromium --version` smoke check fails docker build EARLY
#   if BrowserFetcher hands us a corrupt or sys-dep-incomplete binary.
# Pitfall 7: --no-sandbox + --disable-dev-shm-usage runtime flags are wired in
#   ComixPlaywrightSigner.LaunchBrowserAsync.
# N-1: this approach intentionally diverges from the upstream PuppeteerSharp blog
#   (`hardkoded.com/blog/puppeteer-sharp-docker`); D-02 + the smoke check are the
#   tradeoff lock.
#
# ── 17-REVIEW.md CR-03 — DEFERRED TO PHASE 99 (2026-05-10 acknowledged) ──
# This Dockerfile is the Debian-PACKAGE BUILDER (note `mono-complete`, `dh-make`,
# `dh-systemd`, `devscripts`, `cli-common-dev` above; CMD `/debian-start.sh` runs
# `debian.sh` to package pre-built `/data/sonarr_bin`). The image users PULL is a
# downstream runtime image (likely external — `linuxserver.io`/`hotio.dev` style)
# whose authoring is owned by Phase 99 (Distribution + v1 Release). At Phase-17
# close 2026-05-10 the planner-side decision is to keep the BrowserFetcher
# pre-download here as a LAYOUT REFERENCE Phase 99 will lift into the runtime
# Dockerfile (sys-deps list + BrowserFetcher invocation pattern + smoke check
# shape are all reusable). The bake itself is harmless in the package-builder
# image — it has no .NET SDK so the `dotnet run` line below is a no-op at build
# time, and `/opt/mangarr-chromium` lives in a layer that the .deb output never
# carries. Phase 99 will:
#   1) Author the runtime Dockerfile (or add Chromium provisioning to the
#      external linuxserver.io/hotio.dev runtime image build).
#   2) Install .NET SDK in the runtime build stage.
#   3) COPY tools/ChromiumPrefetch/ into the build stage.
#   4) Run the prefetch + smoke check.
#   5) Copy /opt/mangarr-chromium into the published runtime stage.
# See ROADMAP.md Phase 99 SC#1 for the canonical schedule entry.
# (When Phase 99 lands, the 17 D-02 docs and the prefetch lines below MAY be
# removed from this builder Dockerfile — but the sys-deps + smoke-check shape
# are useful enough as on-disk reference that we leave them for now.)

# Sys-deps list extracted from pptr.dev/troubleshooting:
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates fonts-liberation \
    libasound2 libatk-bridge2.0-0 libatk1.0-0 libc6 libcairo2 libcups2 \
    libdbus-1-3 libexpat1 libfontconfig1 libgbm1 libgcc1 libglib2.0-0 \
    libgtk-3-0 libnspr4 libnss3 libpango-1.0-0 libpangocairo-1.0-0 \
    libstdc++6 libx11-6 libx11-xcb1 libxcb1 libxcomposite1 libxcursor1 \
    libxdamage1 libxext6 libxfixes3 libxi6 libxrandr2 libxrender1 \
    libxss1 libxtst6 lsb-release wget xdg-utils libdrm2 libxkbcommon0 \
 && rm -rf /var/lib/apt/lists/*

# D-03: bake Chromium at /opt/mangarr-chromium — fixed image-layer path.
ENV PUPPETEER_CACHE_DIR=/opt/mangarr-chromium

# Build-time pre-download. tools/ChromiumPrefetch/ csproj is the build-time helper
# from Plan 17-01 Task 1. Runs against the .NET SDK provisioned in this stage.
RUN dotnet run --project tools/ChromiumPrefetch/ChromiumPrefetch.csproj \
      --configuration Release \
      -- --output-dir /opt/mangarr-chromium

# Pitfall 1 mitigation: smoke check the bundled binary BEFORE the image is published.
# BrowserFetcher emits to a versioned subdirectory under PUPPETEER_CACHE_DIR (e.g.
# /opt/mangarr-chromium/chrome/linux-<rev>/chrome-linux64/chrome); use a find-based
# discovery so we don't hardcode the revision.
RUN CHROME_BIN="$(find /opt/mangarr-chromium -type f -name 'chrome' -perm -u+x | head -n1)" \
 && [ -n "$CHROME_BIN" ] || (echo "FATAL: BrowserFetcher produced no chrome exe under /opt/mangarr-chromium" && exit 1) \
 && "$CHROME_BIN" --version \
 || (echo "FATAL: Chromium binary corrupted or missing sys-deps" && exit 1)
# ── /Phase 17 ──

COPY debian-start.sh /debian-start.sh
RUN  fromdos /debian-start.sh

WORKDIR /data
VOLUME [ "/data/sonarr_bin", "/data/build", "/data/output" ]
CMD /debian-start.sh
