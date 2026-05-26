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

## Microsoft.Playwright

**Project:** Microsoft.Playwright — .NET browser automation library
**Version:** 1.59.0
**NuGet:** https://www.nuget.org/packages/Microsoft.Playwright
**Source:** https://github.com/microsoft/playwright-dotnet
**License:** Apache-2.0
**Mangarr usage:** Phase 33.3 Comix runtime signer (`src/NzbDrone.Core/Indexers/Comix/ComixPlaywrightSigner.cs`) drives an embedded Chromium child (headed under Xvfb) to run comix.to's env-module decryption oracle. **Replaces PuppeteerSharp 24.42.0** (Phase 17–33.2): PuppeteerSharp issues CDP `Runtime.enable`, a Cloudflare `challenge-platform` bot-detection vector that prevented the signer from clearing comix.to's managed challenge; Playwright defers it via isolated worlds and cold-solves the challenge (see `.planning/phases/33.3-…/`). PuppeteerSharp was removed — the signer was its only consumer.
**Modifications:** none (NuGet dep; no source-port modifications).

Apache-2.0 license boilerplate: see https://www.apache.org/licenses/LICENSE-2.0.

## Chromium

**Project:** Chromium open-source web browser
**Version:** Playwright-pinned revision (matches Microsoft.Playwright 1.59.0; baked by the official `mcr.microsoft.com/playwright/dotnet:v1.59.0-noble` image)
**Source:** https://www.chromium.org/chromium-projects/
**License:** BSD-3-Clause
**Mangarr usage:** Phase 33.3 — Playwright's bundled Chromium baked at `/ms-playwright` in the Mangarr Docker image (`PLAYWRIGHT_BROWSERS_PATH`; D-01, D-03 — single image, fixed image-layer path). Spawned lazily by `ComixPlaywrightSigner` (headed under Xvfb) on first comix.to request; idle-teardown after 10 minutes. (Phase 17–33.2 baked it at `/opt/mangarr-chromium` via PuppeteerSharp's BrowserFetcher — retired in Phase 33.3.)
**Modifications:** none (binary distribution via the official Playwright .NET image).

BSD-3-Clause license: see https://opensource.org/licenses/BSD-3-Clause.

## Comix Signer (keiyoushi Comix.kt `captureToken()`)

**Upstream source path (CURRENT — captureToken):** `keiyoushi/extensions-source/src/en/comix/src/eu/kanade/tachiyomi/extension/en/comix/Comix.kt` (function `captureToken` at lines 414-471 of head)
**Upstream commit SHA at port time (CURRENT):** `965dc242` (2026-05-12 — "Comix: only get token via webview"). Captured 2026-05-22 via `gh api repos/keiyoushi/extensions-source/commits/main --jq '.sha'` during the `.planning/debug/comix-signer-rotation.md` investigation.
**Historical (Phase 17 port-time) upstream source path:** `keiyoushi/extensions-source/src/en/comix/src/eu/kanade/tachiyomi/extension/en/comix/Signer.kt` (DELETED upstream in commit `965dc242`).
**Historical (Phase 17 port-time) upstream commit SHA:** `9ceeab04a7950173bb73a4e5b6f29cef5ded1457` (captured 2026-05-10; preserved as the historical port baseline). `src/NzbDrone.Core.Test/Indexers/Comix/Resources/upstream-signer.txt` carries the SHA-pinned snapshot of the now-deleted `Signer.kt` for drift-history reference.
**Mangarr ported file:** `src/NzbDrone.Core/Indexers/Comix/ComixPlaywrightSigner.cs` (renamed from `ComixPuppeteerSigner.cs` in Phase 33.3)
**License:** Apache 2.0
**Modifications:**
- Translated from Kotlin (Android `WebView` + `WebViewClient.shouldInterceptRequest`) to C#. The browser driver is Microsoft.Playwright .NET (Phase 33.3; was PuppeteerSharp Phase 17–33.2). The current architecture (2026-05-23 env-module-oracle pivot) supersedes the request-interception captureToken approach — it dynamic-imports comix.to's `manga-*` bundle from page context and invokes the bundle's own decrypting axios client (`page.EvaluateAsync`).
- Single-process `IComixSigner` singleton replacing the upstream's per-source `Signer` companion-object pattern; lifecycle wired to `IHandle<ApplicationShutdownRequested>` for clean teardown.
- Idle-teardown timer added (D-12 — TimeSpan.FromMinutes(10) hardcoded const for v1.0); upstream Android WebView lacks a comparable shape (lifecycle managed by Android's Activity).
- .NET-side serialization via `SemaphoreSlim(1,1)` mirrors upstream's single-thread WebView posture.
- Lazy reprobe-on-EvaluateAsync-error layered on top of upstream's capture-once posture (Phase 17 B-2 path (a) revision — D-09 deferral retired).
- Per-`pageUrl` token cache with 5-minute TTL (Mangarr-only — pagination loop optimization; upstream re-captures per request because Android WebView spin-up is cheap, but PuppeteerSharp page loads are 3-8s).
- 2026-05-22 captureToken rewrite: `captureToken()` ported verbatim from upstream `Comix.kt:414-471` (commit `965dc242`). Two call sites preserved: `/manga/{hid}/chapters` → load `https://comix.to/title/{hid}` (match `/api/v1/manga/{hid}/chapters`); `/chapters/{chapterId}` → load `https://comix.to/chapters/{chapterId}` (match `/api/v1/chapters/{chapterId}`). After token capture, the actual API GET is relayed through the page's same-origin `fetch()` (Choice A per `.planning/debug/comix-signer-rotation.md`) — preserves the existing `IComixSigner.ProxyFetchAsync` contract.
- Replaces the prior Phase 17 namespace-probe approach (PROBE_JS walker against `Object.keys(window)`). The upstream `Signer.kt` file referenced by the Phase 17 port was DELETED in commit `965dc242` when the captureToken pattern landed.

**Apache 2.0 license boilerplate:** See <https://www.apache.org/licenses/LICENSE-2.0>.
The full Apache-2.0 license text is not inlined here; reference link satisfies §4(a) per discretion of the porter (no NOTICE file in upstream).
