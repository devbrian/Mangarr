# Mangarr v1.0.0

Welcome to the first public release of Mangarr — the *arr-style automator the manga community has been waiting for. This document is the launch announcement; auto-generated changelogs handle subsequent v1.0.0.N+ follow-ups.

## What is Mangarr?

Mangarr is a manga / manhwa / manhua library manager and downloader. You add a title once, and Mangarr monitors source sites for new chapters, downloads them, organizes them on disk, and notifies your reader of choice (Komga, Kavita) so the chapter shows up the next time you open the app. It is the missing automation layer between the source sites and the readers — exactly the role Sonarr plays for TV in the *arr-ecosystem.

Mangarr is a **fork of [Sonarr](https://github.com/Sonarr/Sonarr) v5**, adapted for the manga domain. It inherits Sonarr's mature event-driven architecture: the ThingiProvider plugin pattern for indexers/download-clients/notifications, the Decision Engine specification pipeline for release scoring, the SignalR push model for real-time UI updates, the FluentMigrator schema layer, and the React + TypeScript shell. The domain model is rewritten end-to-end (`Manga` / `Chapter` / `ChapterFile` replacing `Series` / `Episode` / `EpisodeFile`; `TranslationProfile` + Custom Formats replacing Sonarr's TV-resolution `Quality` enum); the Sonarr-shaped infrastructure underneath is preserved verbatim where it works.

**v1.0.0 marks Mangarr as a permanent hard fork.** No further upstream merges from Sonarr's `v5-develop` are planned (the `v5-develop` branch is kept as a read-only mirror for blame archaeology / occasional cherry-pick reference). The structural delta vs Sonarr v5 — captured in detail in [`DIVERGENCE.md`](./DIVERGENCE.md) — is now too large to batch-merge productively without introducing regressions. This is the same trade-off Whisparr accepted at its v3 line and that Readarr's eventual retirement underscored as unavoidable for any *arr-fork that diverges past a critical mass.

## Install

The primary install path is the first-party Docker image published to GitHub Container Registry. The image is `linux/amd64` + `linux/arm64` multi-arch — `docker pull` automatically resolves the right manifest for your host.

### Docker (recommended)

Pull the v1.0.0 image:

```bash
docker pull ghcr.io/devbrian/mangarr:1.0.0
```

One-shot `docker run` example, mapping the standard *arr-ecosystem volumes + env vars:

```bash
docker run -d \
  --name mangarr \
  -p 8989:8989 \
  -v ~/mangarr-config:/config \
  -v ~/manga:/data \
  -e PUID=1000 \
  -e PGID=1000 \
  -e UMASK=002 \
  -e TZ=Etc/UTC \
  --restart unless-stopped \
  ghcr.io/devbrian/mangarr:1.0.0
```

Equivalent `docker-compose.yml` snippet:

```yaml
services:
  mangarr:
    image: ghcr.io/devbrian/mangarr:1.0.0
    container_name: mangarr
    ports:
      - "8989:8989"
    volumes:
      - ~/mangarr-config:/config
      - ~/manga:/data
    environment:
      - PUID=1000
      - PGID=1000
      - UMASK=002
      - TZ=Etc/UTC
    restart: unless-stopped
```

Once the container is up, browse to `http://<host>:8989` to finish setup (set an authentication method on the first-run wizard, add MangaDex + Comix as sources, connect Komga or Kavita under Settings → Connect).

### Available image tags

The full hotio-style tag stack is pushed per release:

| Tag                              | Use when you want…                                    |
| -------------------------------- | ----------------------------------------------------- |
| `ghcr.io/devbrian/mangarr:1.0.0.42` | Exact reproducibility — pin to a specific build      |
| `ghcr.io/devbrian/mangarr:1.0.0` | Latest 1.0.0 patch level                              |
| `ghcr.io/devbrian/mangarr:1.0`   | Latest 1.0.x                                          |
| `ghcr.io/devbrian/mangarr:1`     | Latest 1.x                                            |
| `ghcr.io/devbrian/mangarr:latest`| Bleeding edge — rolls forward across major versions   |

### Bonus content: per-platform runtime archives

In addition to the Docker image, each GitHub Release attaches **10 pre-built runtime archives** as bonus content: `freebsd-x64`, `linux-arm`, `linux-arm64`, `linux-musl-arm64`, `linux-musl-x64`, `linux-x64`, `osx-arm64`, `osx-x64`, `win-x64`, `win-x86`. These are unwrapped binaries — extract and run; no installer. They are inherited from Sonarr's cross-compilation matrix and ship as a convenience for users on platforms where Docker isn't a fit. Official OS-native installers (`.deb` / `.rpm` / `.msi` / macOS `.app`) are a v1.x deliverable; v1.0.0 ships the runtime archives only.

## Supported sources

v1.0.0 ships with **two manga sources**:

- **[MangaDex](https://mangadex.org/)** — the v1 bedrock. The largest community-driven manga catalog with the broadest manga / manhwa / manhua coverage and a documented public API. MangaDex doubles as both Mangarr's primary metadata source AND an indexer (chapter discovery).
- **[Comix](https://comix.to/)** — the v1 reference port (the first non-MangaDex aggregator to validate Mangarr's source onboarding methodology). Comix uses a PuppeteerSharp-driven runtime request signer (Phase 17) to navigate its anti-bot signing path.

**MangaFire** was originally on the v1 named slate but is deferred to v2 (per Phase 3 D-19 + the v2 [SOLVE-01](./.planning/REQUIREMENTS.md) dependency). MangaFire's keiyoushi-extension is a HYBRID HTML+WebView shape that conflicts with Mangarr's Phase 3 source-onboarding contract; it reopens once the v2 SOLVE-01 anti-bot solver lands.

**The Tachiyomi-extension runtime shim** (executing keiyoushi-style Kotlin extensions in-process) stays explicitly out-of-scope for v1.x; it is a v3+ consideration. The *port-source* methodology (manual C# ports of Apache-2.0 Kotlin code per `.planning/decisions/source-onboarding-methodology.md`) IS the v1 path, and is how Comix shipped.

## Reader integrations

v1.0.0 fans out an `OnChapterImport` notification to **two readers** out of the box:

- **[Komga](https://komga.org/)** — Phase 6 `KomgaNotification` triggers a library rescan via Komga's REST API the moment a chapter import completes.
- **[Kavita](https://www.kavitareader.com/)** — Phase 6 `KavitaNotification` does the same against Kavita's REST API.

The notification contract is the existing Sonarr `INotification` interface; future reader integrations (Mihon, Calibre OPF, custom webhook) are v2 candidates via the `IMetadataWriter` plugin pattern per [ARCHIVE-04](./.planning/REQUIREMENTS.md). Sonarr's broader notification surface (Discord, Email, Slack, Telegram, Pushover, Pushbullet, Plex, Emby, etc.) is **reference-preserved** under `.planning/reference/sonarr-vertical-slices/notifications-extra/` for v2 to translate into manga peers; the infrastructure (`NotificationBase`, factory, REST controller, frontend Settings page) is live for Komga + Kavita today.

## Known limitations

A small set of intentional gaps at the v1.0.0 launch — each is scheduled for v1.x unless noted.

- **GHCR-only at v1.0.0.** Docker Hub mirror is a v1.x consideration once first-party GHCR publishing is validated in production. There is currently no mirror; pulls must resolve `ghcr.io/devbrian/mangarr`.
- **No in-app update broker.** Mangarr does not yet wire an equivalent of Sonarr's `services.sonarr.tv` update channel. Updates surface via GitHub Releases — subscribe to the repo's release feed for notifications. An in-app version check pointing at the GitHub Releases API is a v1.x deliverable.
- **`v5-develop` is a read-only mirror.** Per the permanent-hard-fork stance (D-13/D-14), community PRs targeting `v5-develop` will not be auto-merged into `Mangarr-v0`. All Mangarr work targets `Mangarr-v0`; useful Sonarr cherry-picks (e.g., a CVE patch in shared infra) are landed manually on a case-by-case basis.
- **No image signing (cosign) or SBOM (syft).** Supply-chain hardening (`cosign sign`, `syft` SBOM generation, `slsa-github-generator` provenance) is a v1.x deliverable. v1.0.0 images carry GHCR's default attestation only.
- **No Discord / community channel at launch.** The inherited Sonarr `notify` job that posted to the Sonarr Discord was removed (D-11) — there is no Mangarr community Discord yet. If community presence justifies it in v1.x, the job will be re-added with a `MANGARR_DISCORD_WEBHOOK_URL` secret.
- **Cloudflare-protected sources require a user-configured solver sidecar.** v1.0.0 does NOT ship a built-in anti-bot solver. Sources protected by Cloudflare's challenge page need a user-configured Byparr or FlareSolverr sidecar in the same Docker network. Built-in solver integration is the v2 SOLVE-01 deliverable.
- **OS-native installers (.deb / .rpm / .msi / .app)** are NOT in v1.0.0. The Docker image is the primary path; per-platform runtime archives ship as bonus content (see [Install](#install)). Official installers are v1.x.
- **First-time GHCR users may need a one-time visibility flip** on the package — see [Package visibility (one-time)](#package-visibility-one-time) below.

## Acknowledgements

Mangarr stands on top of a decade of work by people who built the foundations the manga community now gets to reuse.

- **The [Sonarr](https://github.com/Sonarr/Sonarr) team** — for the v5 codebase that Mangarr forked from. Mangarr inherits Sonarr's ThingiProvider plugin pattern (auto-discovery of indexers, download clients, notifications, metadata sources, specifications, health checks via reflection), the Decision Engine specification pipeline, the SignalR push model, the FluentMigrator schema layer, the React + TypeScript shell, the Custom Formats engine, the entire NLog logging stack, the DryIoc DI container conventions — and the broader idea that "*arr-style automation" is a coherent UX category in its own right.
- **The [keiyoushi](https://github.com/keiyoushi) maintainers** — for the [upstream Mihon extensions](https://github.com/keiyoushi/extensions-source) that Mangarr's source onboarding pipeline uses as the canonical port reference. The Comix port that ships in v1.0.0 was translated from keiyoushi's Apache-2.0 Kotlin source per `.planning/decisions/source-onboarding-methodology.md` §2.
- **The [hotio](https://hotio.dev/) and [linuxserver.io](https://www.linuxserver.io/) maintainers** — for establishing the *arr-ecosystem Docker convention that Mangarr's image follows (`PUID` / `PGID` / `UMASK` / `TZ` env vars; `/config` for application state; `/data` for the library volume; s6-overlay v3 for process supervision). Mangarr clones the **user-facing convention** without inheriting the Alpine base image — PuppeteerSharp's bundled Chromium needs glibc, so Mangarr ships on Debian bookworm-slim per Phase 17 N-1 + Phase 21 RESEARCH Pitfall 4.
- **The [just-containers](https://github.com/just-containers) team** — for s6-overlay v3, the supervision tree that runs the Mangarr process inside the container.
- **The three dormant prior-art "Mangarr" GitHub repos** — `donderjoekel/Mangarr` (archived 2025-04-30), `hyminix/Mangarr`, `tnrd-org/Mangarr`. All three projects predate this fork; this Mangarr retains the *arr-naming-convention name despite the collision because all three prior-art repos are inactive and the *arr-suffix convention is too strong a UX signal to abandon. The acknowledgement here closes the BRAND-03 trail.

And **everyone who tested pre-release builds, filed issues, and shaped the v1 scope**. Mangarr exists because the manga community decided it was worth building.

## Package visibility (one-time)

If `docker pull ghcr.io/devbrian/mangarr:1.0.0` returns a 404, the GHCR package may still be visibility-private (a default state for new packages pushed via GitHub Actions). This is a one-time per-repo flip:

1. Visit https://github.com/devbrian/Mangarr/pkgs/container/mangarr
2. Open **Package settings** in the sidebar
3. Scroll to the **Danger Zone** → **Change package visibility**
4. Select **Public** → confirm with the package name

After the flip, the package is publicly pullable forever (subsequent pushes inherit the visibility setting). This note exists in the release notes themselves so future maintainers don't repeat the discovery; the same instruction is also captured in Phase 21 RESEARCH Pitfall 2 for the planning trail.

---

*Mangarr v1.0.0 — released 2026-MM-DD. Tag: `v1.0.0.N`. Branch: `Mangarr-v0`. Built from commit `<SHA>` via `.github/workflows/build_v5.yml` → `deploy.yml` (Phase 21 D-04..D-12).*
