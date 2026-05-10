# Mangarr

A manga / manhwa / manhua library manager and downloader. Mangarr monitors manga reader and aggregator websites for new chapters of your favorite titles, automatically downloads, sorts, and organizes them. It can also be configured to automatically upgrade quality when better scans become available.

Mangarr is a fork of [Sonarr](https://github.com/Sonarr/Sonarr), adapting Sonarr's mature TV-show management infrastructure to manga management.

## Status

Mangarr is in active development. v1.0 is the first public release; pre-v1.0 schema-mutability semantics apply per [`.planning/decisions/dev-migration-policy.md`](./.planning/decisions/dev-migration-policy.md) (the schema baseline migration `001_mangarr_baseline.cs` is mutable in place until the v1.0.0 tag).

## Features (v1)

- Add manga titles from MangaDex / AniList / MyAnimeList
- Monitor / unmonitor manga and individual chapters
- Configurable chapter sources (MangaDex, comix.to in v1)
- In-process downloader (no external client required)
- CBZ + folder-of-images output formats
- Custom Formats + TranslationProfile-based release ranking
- Komga + Kavita rescan notifications on import
- REST API + SignalR real-time push (`/api/v5/...`)

## Prior Art Acknowledgement

Three dormant prior-art "Mangarr" GitHub repositories exist; this project is independent and not derived from any of them:

- [`donderjoekel/Mangarr`](https://github.com/donderjoekel/Mangarr) — archived 2025-04-30
- [`hyminix/Mangarr`](https://github.com/hyminix/Mangarr)
- [`tnrd-org/Mangarr`](https://github.com/tnrd-org/Mangarr)

These projects share only the name. This Mangarr is a downstream fork of Sonarr v5 — see [DIVERGENCE.md](./DIVERGENCE.md) for divergence details.

## Documentation

- [PROJECT.md](./.planning/PROJECT.md) — vision + scope + design philosophy
- [REQUIREMENTS.md](./.planning/REQUIREMENTS.md) — v1 requirements (71 across 19 categories)
- [ROADMAP.md](./.planning/ROADMAP.md) — phase plan
- [DIVERGENCE.md](./DIVERGENCE.md) — intentional divergences from upstream Sonarr
- [CLAUDE.md](./CLAUDE.md) — codebase guide for AI-assisted development

## Building from Source

### Prerequisites

- .NET SDK 10.0.203 (`winget install Microsoft.DotNet.SDK.10 --source winget`)
- Node.js 20.x or higher
- Yarn (enable with `corepack enable`)

### Build + Run

```bash
# 1. Install frontend deps
yarn install

# 2. Build backend & frontend
dotnet build src/Mangarr.sln --configuration Debug
yarn build

# 3. Run
dotnet run --project src/NzbDrone.Console/Mangarr.Console.csproj
```

App listens at **http://localhost:8989**.

## Default Data Dir

- Linux / Mac: `~/.config/Mangarr`
- Windows: `C:\ProgramData\Mangarr`

API key auto-generated on first run; check `<data-dir>/config.xml` or General Settings.

### Comix indexer (optional)

The Comix (`comix.to`) indexer is included by default. It uses an embedded headless Chromium browser to handle comix.to's anti-bot signing — **no manual setup is required**. Chromium is bundled into the official Mangarr Docker image at `/opt/mangarr-chromium` (~150MB image-size addition; lazy-spawn at runtime — Chromium only starts after the first Comix request, idle-teardown after 10 minutes).

For development outside Docker, run `dotnet run --project tools/ChromiumPrefetch/ChromiumPrefetch.csproj -- --output-dir ~/.cache/mangarr-chromium` once and set `PUPPETEER_CACHE_DIR=~/.cache/mangarr-chromium` before launching Mangarr.

## License

Mangarr inherits Sonarr's GPL-3.0 license. See [LICENSE.md](./LICENSE.md).

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md) and [CLA.md](./CLA.md).

---

**Heritage note.** Mangarr is a fork of Sonarr; the codebase preserves the `NzbDrone.*` directory + namespace prefix on the historic projects per the design philosophy *"Preserve Sonarr's shape wherever it works; diverge only where the manga domain forces us."* See [DIVERGENCE.md](./DIVERGENCE.md) for the divergence catalog and [`.planning/PROJECT.md` § Reference Preservation Policy](./.planning/PROJECT.md#reference-preservation-policy) for v2-deferred features preserved as Sonarr vertical-slice references under `.planning/reference/sonarr-vertical-slices/`.
