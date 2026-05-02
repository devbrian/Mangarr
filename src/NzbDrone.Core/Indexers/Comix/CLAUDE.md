# Indexers/Comix (Phase 3 — v1 reference port #1)

## Purpose

Port of `keiyoushi/extensions-source/src/en/comix/Comix.kt` (Apache-2.0; PR #11658 merged 2025-11-16). comix.to ships a clean JSON API at `/api/v2/...` — D-09 reverse-engineer-API-first cleanly satisfied; D-10 (no HTML parser dep) and D-13 (no browser automation) honored. `THIRD-PARTY-NOTICES.md` (created in Plan 03-06) records the keiyoushi commit SHA at port time per source-onboarding-methodology.md §4.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Indexers\Comix`

## Key Files

| File | Purpose |
|------|---------|
| `ComixIndexer.cs` | Concrete `HttpAggregatorBase<ComixIndexerSettings>` plugin. Implements `Fetch(MangaSearchCriteria)` + `Fetch(ChapterSearchCriteria)` (calls `FetchReleases` directly, not `base.Fetch` — abstract override forbids that per Plan 03-04 lessons). 7 inherited TV overloads throw `NotSupportedException` (D-03). `GetDownloadHeaders` returns `{"Referer": "https://comix.to/"}` (D-14 — keiyoushi pattern). Constructor injects `IIndexerSourceStatusService` (Plan 03-03 D-17 sibling). |
| `ComixIndexerSettings.cs` | `IHttpAggregatorSettings` impl. **`UserAgentOverride` IS exposed via `[FieldDefinition]`** (UNLIKE MangaDex — Cloudflare workaround per RESEARCH.md anti-bot section). Default `RateSeconds=0.2` (5 req/s — keiyoushi `rateLimit(5)` verbatim). |
| `ComixRequestGenerator.cs` | URL composition: `/api/v2/manga?order[chapter_updated_at]=desc` (FetchRecent) + `/api/v2/manga/{hash}/chapters` (search). 7 TV overloads return empty `IndexerPageableRequestChain` (D-03 fan-out). Hash key derived from `Manga.CleanTitle` → slug fallback (Manga model has no `ComixHash` field; v2 metadata-source linkage will replace with canonical `hash_id` lookup). |
| `ComixParser.cs` | JSON parser (Newtonsoft). Auto-detects response shape (manga-list vs chapter-list) by probing first item's keys (`chapter_id` vs `manga_id`+`hash_id`). Populates `ReleaseInfo.ScanlationGroup` from `scanlation_group.name` when present (null on official rows; `is_official=1`). Sets `TranslatedLanguage="en"` hard-coded (comix.to is single-language English-only per keiyoushi `lang` setting). `DownloadUrl=https://comix.to/api/v2/chapters/{chapterId}` — chapter MANIFEST URL (NOT a single image; Phase 4 dereferences). |
| `ComixDto.cs` | Newtonsoft POCOs aligned with ACTUAL Wave 0 fixture envelope shape (`{status, result:{items, pagination}}`) — diverges from plan literal (`data`, `current_page`, `last_page`) which targeted a non-existent live API; the synthesized fixtures are the contract per `SOURCE-PROBE-fixtures.md`. Generic `ComixResponse<TItem>` envelope + concrete aliases `ComixMangaListResponse` + `ComixChapterListResponse`. |

## Patterns / Conventions

- ThingiProvider auto-discovery via reflection (no manual DI wiring) — registers automatically by extending `HttpAggregatorBase<TSettings>`.
- Rate budget: `RateSeconds=0.2` (200ms gap = 5 req/s — matches keiyoushi `rateLimit(5)` verbatim). SourceKey="comix.to" budget shared with Phase 4 image-fetch (single bucket; no fragmentation).
- Per-`SourceKey` escalation: shared with Plan 03-03 `IIndexerSourceStatusService` — failure pressure on a CF challenge disables the SourceKey for hours; surfaces via Health Check warning (Plan 03-03 IndexerSourceFailureCheck).
- `GetDownloadHeaders` returns `{"Referer": "https://comix.to/"}` — Phase 4 in-process downloader applies on each image GET (D-14).
- Anti-bot posture: comix.to is Cloudflare-protected; UA override IS exposed for users to spoof per RESEARCH.md anti-bot section. No solver plumbing in Phase 3 (D-11 / D-13 — defer to v2 SOLVE-01).
- Single-source convention: `ScanlationGroup` populated from chapter row when present (community translations); `null` on official rows (`is_official=1`). `TranslatedLanguage="en"` hard-coded — comix.to chapter rows do NOT carry a language code; the source is English-only per keiyoushi `lang` setting.
- Fetch delegation: `Fetch(MangaSearchCriteria)` / `Fetch(ChapterSearchCriteria)` call `FetchReleases(g => g.GetSearchRequests(searchCriteria))` directly — `HttpAggregatorBase` declares them `abstract override` (Plan 03-02 D-02), so `base.Fetch(...)` cannot resolve (CS0205). Mirrors MangaDexIndexer (Plan 03-04) verbatim.
- DTO envelope: ACTUAL fixture shape is `{status, result:{items[], pagination}}`; the plan literal (`{data:[], current_page, last_page}`) targeted an assumed-but-non-existent shape and was corrected at port time per `<read_first>` assertion alignment with Wave 0 fixtures.

## Manga Adaptation Notes

- comix.to does NOT have a Phase 2 metadata source counterpart — Manga.CleanTitle/Title is slugified as the search key (vs MangaDex's UUID `MangaDexId`). v2 may add a comix.to metadata source for cross-resolution + canonical `hash_id` lookup via `/api/v2/manga/{mangaHash}/chapters`.
- Phase 3 minimum-viable: indexer can fetch latest updates + search by slugified title; chapter-by-chapter fetch via canonical hash_id deferred (Phase 6 wires once cross-resolution exists).
- SourceKey="comix.to" verbatim (with the dot); `RateLimitKey` is a string and IRateLimitService bucketing handles it. If FluentValidation/Settings UI rejects the dot, fall back to "comixto" and document divergence in `SOURCE-PROBE-comix.md` (Plan 03-06).
- Live API capture during plan execution remains BLOCKED by Cloudflare cookie + RC4 hash-token bypass (per `SOURCE-PROBE-fixtures.md`); production tests run against synthesized fixtures (keiyoushi `Dto.kt` shape). Plan 03-06 daily-cron soak workflow is the live-drift early-warning system.

## Cross-References

- Sonarr analog (closest): `src/NzbDrone.Core/Indexers/Nyaa/Nyaa.cs` (single-source HttpIndexerBase plugin)
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
