# THIRD-PARTY-NOTICES

This file records attributions for code ported into Mangarr from third-party projects.

Mangarr is licensed under [GPL-3.0](LICENSE.md). Apache-2.0-licensed source code from
[`keiyoushi/extensions-source`](https://github.com/keiyoushi/extensions-source) has been
manually ported to C# under the one-way Apache→GPL compatibility documented at
<https://www.apache.org/licenses/GPL-compatibility.html>.

`keiyoushi/extensions-source` ships no `NOTICE` text file (verified 2026-05-01 against the
Codeberg mirror at <https://codeberg.org/keiyoushi/extensions-source>); Apache-2.0 §4(d)
NOTICE-propagation is therefore not triggered. This file is **Mangarr's own attribution
register** — not an Apache-2.0 §4(d) NOTICE relay. See
[.planning/decisions/source-onboarding-methodology.md §4](.planning/decisions/source-onboarding-methodology.md#4-apache-20-attribution).

## MangaDex

Mangarr's `src/NzbDrone.Core/Indexers/MangaDex/` (Phase 3) and
`src/NzbDrone.Core/MetadataSource/MangaDex/` (Phase 2) are **direct ports from the MangaDex
API documentation at <https://api.mangadex.org/docs>**. They are NOT derived from a
`keiyoushi/extensions-source` Kotlin extension. No third-party code attribution applies.

## Comix (comix.to)

**Upstream source path:** `keiyoushi/extensions-source/src/en/comix/src/eu/kanade/tachiyomi/extension/en/comix/Comix.kt`
**Upstream commit SHA at port time:** `20a334b49c6a98de615a01fb2cf8a4b705d0ee22` (captured 2026-05-02 via `gh api repos/keiyoushi/extensions-source/commits/main --jq '.sha'`)
**Mangarr ported file:** `src/NzbDrone.Core/Indexers/Comix/`
**Modifications:**
- Translated from Kotlin (extends `eu.kanade.tachiyomi.source.online.HttpSource`) to C#
  (extends `NzbDrone.Core.Indexers.Http.HttpAggregatorBase<TSettings>`)
- JSON DTO types renamed to C# conventions (`Comix.kt` `ComixDto.kt` → `ComixDto.cs` with
  Newtonsoft `[JsonProperty]` attributes for snake_case mapping)
- Mihon `OkHttpClient.rateLimit(5)` interceptor reimplemented via Phase 1
  `HttpAggregatorBase` SourceKey-keyed `IRateLimitService` integration
  (RateSeconds=0.2 = 5 req/s preserved verbatim)
- WebView-based affordances: NONE used by upstream comix.to extension; Phase 3 D-13
  binding honored cleanly
- HTML scraping: NONE used (comix.to is JSON-API only); Phase 3 D-10 binding honored
- Cloudflare client: dropped — comix.to plugin uses default `IHttpClient`; user may spoof
  UA via Settings field per Phase 3 D-11 if low-grade UA blocks surface

**Apache 2.0 license boilerplate:** See <https://www.apache.org/licenses/LICENSE-2.0>.
The full Apache-2.0 license text is not inlined here; reference link satisfies §4(a) per
discretion of the porter (no NOTICE file in upstream).

## MangaFire

**Status:** Deferred to v2 per Phase 3 D-19 (resolved 2026-05-02 from RESEARCH.md §Open
Question Q-1). The keiyoushi MangaFire extension (`keiyoushi/extensions-source/src/all/mangafire/`)
is a HYBRID HTML scraper + WebView interception (VRF token extraction). The page-list path
requires a real browser, conflicting with Phase 3 D-10 (no HTML parser dep) and D-13 (no
browser automation). MangaFire reopens in v2 when SOLVE-01 (FlareSolverr-API / Byparr-compatible
solver) ships and unblocks WebView-required sources.

When MangaFire ships in v2, this section will be populated with the upstream source path,
commit SHA at port time, ported file location, and modifications list per methodology §4.
