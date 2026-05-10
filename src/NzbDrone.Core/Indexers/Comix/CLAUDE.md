# Indexers/Comix (Phase 3 — v1 reference port #1)

## Purpose

Port of `keiyoushi/extensions-source/src/en/comix/Comix.kt` (Apache-2.0; PR #11658 merged 2025-11-16). comix.to ships a clean JSON API at `/api/v2/...` — D-09 reverse-engineer-API-first cleanly satisfied; D-10 (no HTML parser dep) and D-13 (no browser automation) honored. `THIRD-PARTY-NOTICES.md` (created in Plan 03-06) records the keiyoushi commit SHA at port time per source-onboarding-methodology.md §4.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Indexers\Comix`

## Key Files

| File | Purpose |
|------|---------|
| `ComixIndexer.cs` | Concrete `HttpAggregatorBase<ComixIndexerSettings>` plugin. Phase 17: `Fetch(MangaSearchCriteria)` + `Fetch(ChapterSearchCriteria)` + `GetChapterPages` dispatch via `IComixSigner.ProxyFetchAsync` (NOT `FetchReleases` / `IHttpClient`) per D-08. `GetDownloadHeaders` returns `{"Referer": "https://comix.to/"}` (D-14 — keiyoushi pattern). Constructor injects `IIndexerSourceStatusService` (Plan 03-03 D-17 sibling) AND `IComixSigner` (Phase 17 D-05). |
| `ComixIndexerSettings.cs` | `IHttpAggregatorSettings` impl. **`UserAgentOverride` IS exposed via `[FieldDefinition]`** (UNLIKE MangaDex — Cloudflare workaround per RESEARCH.md anti-bot section). Default `RateSeconds=0.2` (5 req/s — keiyoushi `rateLimit(5)` verbatim). |
| `ComixRequestGenerator.cs` | Phase 17 Path A: path composition (NOT URL-with-token). `/api/v1/manga?order[chapter_updated_at]=desc` (FetchRecent — unsigned) + `BuildChapterListPath('/manga/{hid}/chapters?...')` populated into `ResolvedSignerPaths` for ComixIndexer.Fetch to dispatch via `_signer.ProxyFetchAsync`. ChapterSearchCriteria appends `&number={chapterNumber}` for server-side per-chapter filtering. Manga model has no canonical hid field; v2 metadata-source linkage will replace with canonical `hash_id` lookup. |
| `ComixParser.cs` | JSON parser (Newtonsoft). Auto-detects response shape (manga-list vs chapter-list) by probing first item's keys. Populates `ReleaseInfo.ScanlationGroup` from `group.name` when present (null on official rows). Sets `TranslatedLanguage="en"` hard-coded (comix.to is single-language English-only). `DownloadUrl=https://comix.to/api/v1/chapters/{chapterId}/pages` — chapter MANIFEST URL (Phase 4 dereferences via `ComixIndexer.GetChapterPages` → signer). |
| `ComixDto.cs` | Newtonsoft POCOs aligned with ACTUAL fixture envelope shape (`{status, result:{items, pagination}}`). Generic `ComixResponse<TItem>` envelope + concrete aliases `ComixMangaListResponse` + `ComixChapterListResponse`. |
| `IComixSigner.cs` | Phase 17 D-05: process-singleton runtime signer interface. Single async method `ProxyFetchAsync(apiPath, ct) → Task<string>` returns the decoded JSON body. Pattern S2 `// Sonarr divergence:` marker (no Sonarr peer). |
| `ComixPuppeteerSigner.cs` | Phase 17 D-05 concrete impl. PuppeteerSharp 24.42.0 + embedded headless Chromium; lazy-spawn warm page; behaviour-based namespace probe (`vmf_*` / response-interceptor detection per upstream `Signer.kt`); idle-teardown after 10 min (D-12); `IHandle<ApplicationShutdownRequested>` for clean teardown; layered LAZY REPROBE on EvaluateAsync error (B-2 path (a)); `SemaphoreSlim(1,1)` serializes per-request EvaluateAsync (D-06) with 5s drain on Dispose (W-2). DryIoc auto-discovered as singleton-via-interface per the existing `RegisterMany` convention (W-3). |

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
