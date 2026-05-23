# Indexers/Comix (Phase 3 — v1 reference port #1)

## Purpose

Port of `keiyoushi/extensions-source/src/en/comix/Comix.kt` (Apache-2.0; PR #11658 merged 2025-11-16). comix.to ships a clean JSON API at `/api/v2/...` — D-09 reverse-engineer-API-first cleanly satisfied; D-10 (no HTML parser dep) and D-13 (no browser automation) honored. `THIRD-PARTY-NOTICES.md` (created in Plan 03-06) records the keiyoushi commit SHA at port time per source-onboarding-methodology.md §4.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Indexers\Comix`

## Key Files

| File | Purpose |
|------|---------|
| `ComixIndexer.cs` | Concrete `HttpAggregatorBase<ComixIndexerSettings>` plugin. Phase 17: `Fetch(MangaSearchCriteria)` + `Fetch(ChapterSearchCriteria)` + `GetChapterPages` dispatch via `IComixSigner.ProxyFetchAsync` (NOT `FetchReleases` / `IHttpClient`) per D-08. `GetDownloadHeaders` returns `{"Referer": "https://comix.to/"}` (D-14 — keiyoushi pattern). Constructor injects `IIndexerSourceStatusService` (Plan 03-03 D-17 sibling) AND `IComixSigner` (Phase 17 D-05). **Phase 17.2 D-3 (WR-GC-01):** `DispatchSignerPathsAsync` and `GetChapterPages` call `TryDetectDecryptErrorEnvelope` on the decoded body before deserializing as defense-in-depth — if the body matches the in-IIFE BRANCH-C catch envelope shape (`{result:null, e:..., decryptError:...}`), the call routes to `IIndexerSourceStatusService.RecordFailure(ComixSourceKey, decryptError)` instead of silently swallowing the failure. **Note (2026-05-22 captureToken rewrite):** the new signer architecture never emits a decryptError envelope; this guard is now a SAFETY NET against any future comix.to encryption re-introduction. |
| `ComixIndexerSettings.cs` | `IHttpAggregatorSettings` impl. **`UserAgentOverride` IS exposed via `[FieldDefinition]`** (UNLIKE MangaDex — Cloudflare workaround per RESEARCH.md anti-bot section). Default `RateSeconds=0.2` (5 req/s — keiyoushi `rateLimit(5)` verbatim). |
| `ComixRequestGenerator.cs` | Phase 17 Path A: path composition (NOT URL-with-token). `/api/v1/manga?order[chapter_updated_at]=desc` (FetchRecent — unsigned) + `BuildChapterListPath('/manga/{hid}/chapters?...')` populated into `ResolvedSignerPaths` for ComixIndexer.Fetch to dispatch via `_signer.ProxyFetchAsync`. ChapterSearchCriteria appends `&number={chapterNumber}` for server-side per-chapter filtering. Manga model has no canonical hid field; v2 metadata-source linkage will replace with canonical `hash_id` lookup. |
| `ComixParser.cs` | JSON parser (Newtonsoft). Auto-detects response shape (manga-list vs chapter-list) by probing first item's keys. Populates `ReleaseInfo.ScanlationGroup` from `group.name` when present (null on official rows). Sets `TranslatedLanguage="en"` hard-coded (comix.to is single-language English-only). `DownloadUrl=https://comix.to/api/v1/chapters/{chapterId}` — chapter DETAIL URL (Phase 4 dereferences via `ComixIndexer.GetChapterPages` → signer). **Phase 17.2 GAP-17-E (2026-05-10):** legacy shape was `/api/v1/chapters/{id}/pages` (separate pages-list endpoint); the bundle's signer allowlist now rejects all `/chapters/{id}/<suffix>` shapes — pages list is embedded in the bare chapter detail body under `result.pages.{baseUrl, items[]}`. See `.planning/phases/17.2-comix-signer-driver-layer-fix/17.2-PAGES-ENDPOINT-SURVEY.md`. |
| `ComixDto.cs` | Newtonsoft POCOs aligned with ACTUAL fixture envelope shape (`{status, result:{items, pagination}}`). Generic `ComixResponse<TItem>` envelope + concrete aliases `ComixMangaListResponse` + `ComixChapterListResponse`. **Phase 17.2 GAP-17-E:** `ComixChapterPagesResponse` reshaped — `Result.Pages.{BaseUrl, Items[{Width,Height,Url}]}` (chapter-detail-rooted; per-page absolute URLs composed `BaseUrl + Items[i].Url` via the `.Pages` accessor). The legacy `Result.Images[{Url:absolute}]` shape no longer exists. |
| `IComixSigner.cs` | Phase 17 D-05: process-singleton runtime signer interface. Single async method `ProxyFetchAsync(apiPath, ct) → Task<string>` returns the decoded JSON body. Pattern S2 `// Sonarr divergence:` marker (no Sonarr peer). Contract unchanged across the 2026-05-22 captureToken rewrite. |
| `ComixPuppeteerSigner.cs` | Phase 17 D-05 concrete impl. PuppeteerSharp 24.42.0 + embedded headless Chromium; lazy-spawn warm page; **captureToken via request interception** (2026-05-22 rewrite — see Phase 17 Invariants below for architecture). Idle-teardown after 10 min (D-12); `IHandle<ApplicationShutdownRequested>` for clean teardown; layered LAZY REPROBE on EvaluateAsync error (B-2 path (a)); `SemaphoreSlim(1,1)` serializes per-request EvaluateAsync (D-06) with 5s drain on Dispose (W-2). DryIoc auto-discovered as singleton-via-interface per the existing `RegisterMany` convention (W-3). |

## Patterns / Conventions

- ThingiProvider auto-discovery via reflection (no manual DI wiring) — registers automatically by extending `HttpAggregatorBase<TSettings>`.
- Rate budget: `RateSeconds=0.2` (200ms gap = 5 req/s — matches keiyoushi `rateLimit(5)` verbatim). SourceKey="comix.to" budget shared with Phase 4 image-fetch (single bucket; no fragmentation).
- Per-`SourceKey` escalation: shared with Plan 03-03 `IIndexerSourceStatusService` — failure pressure on a CF challenge disables the SourceKey for hours; surfaces via Health Check warning (Plan 03-03 IndexerSourceFailureCheck).
- `GetDownloadHeaders` returns `{"Referer": "https://comix.to/"}` — Phase 4 in-process downloader applies on each image GET (D-14).
- Anti-bot posture: comix.to is Cloudflare-protected; UA override IS exposed for users to spoof per RESEARCH.md anti-bot section. No solver plumbing in Phase 3 (D-11 / D-13 — defer to v2 SOLVE-01).
- Single-source convention: `ScanlationGroup` populated from chapter row when present (community translations); `null` on official rows (`is_official=1`). `TranslatedLanguage="en"` hard-coded — comix.to chapter rows do NOT carry a language code; the source is English-only per keiyoushi `lang` setting.
- Fetch delegation (Phase 17 Path A): `Fetch(MangaSearchCriteria)` / `Fetch(ChapterSearchCriteria)` cast the generator to `ComixRequestGenerator`, populate `ResolvedSignerPaths` via `GetSearchRequests`, then loop those paths through `_signer.ProxyFetchAsync`. The `FetchReleases` pipeline is bypassed entirely for the chapter-list endpoint because comix.to's anti-bot signing rotated into obfuscated browser-side JS on 2026-05-09; the runtime signer handles signing in-page. `GetChapterPages` follows the same pattern (per RESEARCH N-3 — manifest endpoint signed; per-image CDN GETs stay on plain `IHttpClient`).
- DTO envelope: ACTUAL fixture shape is `{status, result:{items[], pagination}}`; the plan literal (`{data:[], current_page, last_page}`) targeted an assumed-but-non-existent shape and was corrected at port time per `<read_first>` assertion alignment with Wave 0 fixtures.

## Manga Adaptation Notes

- comix.to does NOT have a Phase 2 metadata source counterpart — Manga.CleanTitle/Title is slugified as the search key (vs MangaDex's UUID `MangaDexId`). v2 may add a comix.to metadata source for cross-resolution + canonical `hash_id` lookup via `/api/v2/manga/{mangaHash}/chapters`.
- Phase 3 minimum-viable: indexer can fetch latest updates + search by slugified title; chapter-by-chapter fetch via canonical hash_id deferred (Phase 6 wires once cross-resolution exists).
- SourceKey="comix.to" verbatim (with the dot); `RateLimitKey` is a string and IRateLimitService bucketing handles it. If FluentValidation/Settings UI rejects the dot, fall back to "comixto" and document divergence in `SOURCE-PROBE-comix.md` (Plan 03-06).
- Live API capture during plan execution remains BLOCKED by Cloudflare cookie + RC4 hash-token bypass (per `SOURCE-PROBE-fixtures.md`); production tests run against synthesized fixtures (keiyoushi `Dto.kt` shape). Plan 03-06 daily-cron soak workflow is the live-drift early-warning system.

## Cross-References

- Mangarr analog (closest): `src/NzbDrone.Core/Indexers/Nyaa/Nyaa.cs` (single-source HttpIndexerBase plugin)
- Phase 1 base: `src/NzbDrone.Core/Indexers/Http/HttpAggregatorBase.cs` (rate budget + honest UA)
- Plan 03-04 sibling: `src/NzbDrone.Core/Indexers/MangaDex/CLAUDE.md` (BEDROCK shape; Comix is the FIRST keiyoushi-derived port)
- Plan 03-02 contract: manga overloads on IIndexer + ReleaseInfo extension
- Plan 03-03 D-17: per-SourceKey escalation service (`IIndexerSourceStatusService`)
- Plan 03-06 governance: `THIRD-PARTY-NOTICES.md` Comix section + `DIVERGENCE.md` Phase 3 entries + `SOURCE-PROBE-comix.md`
- keiyoushi PR #11658: <https://github.com/keiyoushi/extensions-source/pull/11658>
- Apache 2.0 license: <https://www.apache.org/licenses/LICENSE-2.0>
- methodology §4: `.planning/decisions/source-onboarding-methodology.md`

## Threats Mitigated

| Threat ID | Mitigation |
|-----------|------------|
| T-INJ-02 | Newtonsoft maps numeric `number` to `decimal?` natively; null-checked before use; `JsonException` on envelope probe → empty list (no exception path). |
| T-INJ-URL | Slug derivation (`ResolveHashKey` → `Slugify`) restricts hash key to `[a-z0-9-]`; URL-encoded; no string concat of raw user input into URL path. |
| T-CF-403 | Cloudflare 403 → auto-disable via `IIndexerSourceStatusService.RecordFailure("comix.to")` (Plan 03-03 D-17) → 4-level escalation (D-18) → IndexerSourceFailureCheck Health Check warning (Plan 03-03 SOURCE-05). User can spoof UA per-instance (D-11 / D-14) to dodge low-grade UA blocks. v2 SOLVE-01 owns harder cases. |
| T-DOS-01 | `RateSeconds=0.2` default (200ms gap = 5 req/s — keiyoushi `rateLimit(5)` verbatim). SourceKey-keyed budget shared with Phase 4 image-fetch. |
| T-API-DRIFT | Wave 0 SYNTHESIZED fixtures (per `SOURCE-PROBE-fixtures.md`) load via `File.ReadAllText`; failures surface DTO field-name divergence at test-time. Daily soak workflow (Plan 03-06) catches post-merge live-API drift. |
| T-SCRAPER-ROT | aggregator scraper rot 30-90 day half-life (Pitfall 1); soak threshold <5% failure% over 7-day window (D-15/D-16). |
| T-NULL-DEREF | Null-safe access throughout `ComixParser` (`?.`, fallback dates, group=null on official rows); empty fields handled gracefully. |

## Phase 17 Invariants — Comix Runtime Signer Port (PuppeteerSharp)

**Trigger:** comix.to rotated anti-bot signing keys + began encrypting response bodies on 2026-05-09; the static `ComixHash.cs` port (Phase 3 D-13 baseline) was structurally unviable post-rotation. See `.planning/debug/comix-invalid-token-403.md` for the root-cause + decision provenance.

**2026-05-22 rotation event (captureToken pivot):** comix.to deployed a bundle rotation between 2026-05-12 and 2026-05-22 that moved the signer function out of `globalThis.<namespace>.<fn>` shape entirely. Concurrently, upstream keiyoushi recognized that namespace-walking is unsustainable (the bundle could rotate to any scope/closure shape and break the probe again) and switched to a structurally simpler design: **don't probe — observe**. Upstream commit `965dc242` (2026-05-12, "Comix: only get token via webview") deletes `Signer.kt` entirely and pivots `Comix.kt` to a `captureToken()` shape. The Phase 17 architecture (process-singleton runtime signer that probes `globalThis.vmf_*` for behaviour-matching signer + installer fns) is upstream-obsolete; the current architecture captures the token by intercepting outgoing HTTP requests on a per-call-site basis. See `.planning/debug/comix-signer-rotation.md` for the full root-cause + decision record.

**Architecture (current — 2026-05-22 captureToken):**
- `IComixSigner` (process singleton via DryIoc `Reuse.Singleton` auto-discovery) owns the embedded headless Chromium lifecycle.
- `ComixPuppeteerSigner` is the only impl; uses PuppeteerSharp 24.42.0 + bundled Chromium baked at `/opt/mangarr-chromium` (D-03).
- Lazy-spawn warm page on first request; idle teardown after 10 minutes (D-12 hardcoded const); clean shutdown via `IHandle<ApplicationShutdownRequested>` with W-2 drain semantics (5-second `_gate.Wait` before `_gate.Dispose`).
- **captureToken via request interception** (replaces the upstream-obsolete PROBE_JS namespace-probe approach): on each `ProxyFetchAsync(apiPath)`:
  1. Resolve `(pageUrl, matchSuffix)` from `apiPath` (two routes: `/manga/{hid}/chapters` → load `/title/{hid}` ; `/chapters/{chapterId}` → load `/chapters/{chapterId}`).
  2. Check per-`pageUrl` token cache (5-min TTL — pagination loops reuse a single capture).
  3. On miss: install a `page.Request += handler` that allows `comix.to/*.js + /api/ + /title/ + /chapters/` requests, aborts everything else, and extracts the `_=<token>` query parameter when the matching outgoing API request fires.
  4. `page.GoToAsync(pageUrl, WaitUntil = DOMContentLoaded)` — the bundle bootstraps + fires its own `/api/v1/...?_=<token>` request shortly after DCL.
  5. Await the captured token, bounded by `CaptureTimeoutSeconds = 30` (mirrors upstream Comix.kt:466).
  6. Relay the actual `/api/v1{apiPath}?_=<token>` GET server-side via `System.Net.Http.HttpClient` (Choice B per debug doc — Choice A relay-through-page returned the `{e:<base64>}` encrypted envelope and is structurally untenable). PR #244 review feedback (Codex P1 + CodeRabbit Major) refined this: the relay reuses a process-singleton `_relayHttpClient`, forwards browser session state onto the relay (cookies via `_page.GetCookiesAsync(comix.to)` + UA via `navigator.userAgent` cached at warm-time), and calls `EnsureSuccessStatusCode()` so non-2xx flows through to lazy-reprobe + `RecordFailure` instead of silently degrading. The captureToken wait is token-only (CodeRabbit Major #7) — the bundle's response body is never consumed by the caller.
- `SemaphoreSlim(1,1)` serializes per-request `EvaluateAsync` (D-06).
- **Lazy reprobe on EvaluateAsync error** (Phase 17 B-2 path (a) — D-09 deferral retired): stale-page failure (page navigated away, intercepted request never fired, etc.) clears the token cache, relaunches once, retries; falls through to `RecordFailure` on second throw.
- `IComixSigner.ProxyFetchAsync` returns the DECODED JSON body (Q-2 / N-2 verdict — preserved contract across the 2026-05-22 rewrite).

**Callsite coverage (D-08 verdict per RESEARCH N-3):**
- `ComixIndexer.Fetch(MangaSearchCriteria)` → `_signer.ProxyFetchAsync("/manga/{hid}/chapters")` → captureToken via `https://comix.to/title/{hid}`.
- `ComixIndexer.Fetch(ChapterSearchCriteria)` → `_signer.ProxyFetchAsync(...)` (mirrors MangaSearchCriteria pattern).
- `ComixIndexer.GetChapterPages(release)` → `_signer.ProxyFetchAsync("/chapters/{id}")` → captureToken via `https://comix.to/chapters/{id}`. (Phase 17.2 GAP-17-E: no `/pages` suffix — pages list embedded in chapter detail.)
- Per-image GETs against `cdn.comix.to/.../*.jpg` STAY on plain `IHttpClient`.

**Deleted:** `ComixHash.cs` + `ComixHashFixture.cs` (Phase 17 — replaced by the runtime signer). PROBE_JS literal + `LaunchAndProbeAsync` namespace-walk body + `EvaluateProxyFetchAsync` in-IIFE signer/installer interpolation + `SignerExprAllowlistRegex` + `_signerExpr`/`_installerExpr` fields + `EvaluateRawAsync` diagnostic seam + `SignerExprForTest`/`InstallerExprForTest` accessors + `GetProbeJsForTest` + Phase 17.2 D-1 networkidle settle (2026-05-22 captureToken rewrite — all upstream-obsolete). Companion fixture deletions: `ComixSignerProbeSameNamespaceFixture.cs` + `ComixPagesEndpointSurveyFixture.cs` (probe-specific).

**Failure escalation:** captureToken timeouts + `Browser.LaunchAsync` failures + lazy-reprobe second-throw all flow through `IIndexerSourceStatusService.RecordFailure("comix.to")` (D-10 + D-11 + B-2 path (a)). Existing 4-level escalation surfaces an `IndexerSourceFailureCheck` Health Check warning at the 3-consecutive threshold.

**Test strategy:**
- Unit: `Mock<IComixSigner>` (D-16) returns canned JSON; existing fixtures (`ComixIndexerFixture`, `ComixGetChapterPagesFixture`, `ComixRequestGeneratorFixture`) extend with the mock SetUp.
- Signer-impl fixtures (Contract, FailSoft, IdleTeardown, Shutdown, LifecycleLogs, **LazyReprobe**, **DryIocResolution**, **UpstreamSignerDrift**, **PlatformCacheFallback**) live at `src/NzbDrone.Core.Test/Indexers/Comix/`. `UpstreamSignerDriftFixture` was rewritten 2026-05-22 to lock the captureToken contract (negative assertions on PROBE_JS / `Object.keys(window)` + positive assertions on `SetRequestInterceptionAsync`, `page.Request +=`, `DOMContentLoaded` navigation, `CaptureTimeoutSeconds`, and the relay-fetch shape). `ComixSignerPlatformCacheFallbackFixture` preserves the Phase 17.2 follow-up Chromium-cache fallback guard outside the deleted PROBE-specific fixture.
- Live-Chromium: `src/Mangarr.Comix.Live.Test/ComixSignerLiveFixture.cs` with `[LiveComix]` category — excluded from standard runs; manual run on the executor's box; CI never runs. `ComixSignerStepwiseDiagnosticFixture.cs` was rewritten 2026-05-22 to drive the two captureToken routes step-by-step instead of probing for signer/installer fn refs.
- Drift detection: `.github/workflows/source-soak.yml` extended (D-19) — Plan 03-06 daily-soak runs the live-Comix smoke on schedule (steps INSIDE `jobs.soak.steps:` array per B-1).

**Sonarr-divergence markers** (Pattern S2 / sonarr-consistency-audit Pattern ι allowlist coverage): on `IComixSigner.cs`, `ComixPuppeteerSigner.cs`, and `ComixIndexer.cs` GetChapterPages signer-route line.
