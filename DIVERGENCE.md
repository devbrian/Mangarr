# DIVERGENCE.md

Mangarr is a fork of Sonarr v5. This document enumerates every intentional divergence from `Sonarr/Sonarr` `v5-develop`, with rationale.

Format: flat table grouped by subsystem. Updated by hand when divergences land or move from "planned" to "merged." This is a **static inventory** — the upstream-sync cadence schedule and the CI divergence-flag job are deferred per CONTEXT.md D-17 and may be reopened in a future milestone. See [.planning/phases/00-pre-code-decisions/00-CONTEXT.md](./.planning/phases/00-pre-code-decisions/00-CONTEXT.md) `<deferred>` section.

Type taxonomy: `extend`, `replace`, `delete`, `new`, `preserve`.

## Preserved Unchanged (Sonarr Wins We Keep)

| Subsystem | Reason We Keep It |
|-----------|-------------------|
| `src/NzbDrone.Core/ThingiProvider/` | Auto-discovery plugin pattern. Manga sources, download clients, metadata sources, notifications, specifications, and health checks slot in unchanged via reflection-based assembly scanning. |
| `src/NzbDrone.Core/DecisionEngine/Specifications/` (pattern, not contents) | `IDownloadDecisionEngineSpecification` shape preserved; specific TV-shaped specs swap in Phase 5 for manga equivalents. |
| Dapper + FluentMigrator | Phase 1 ships fresh manga baseline migration; ORM + migration pattern unchanged. |
| `src/NzbDrone.SignalR/` | Real-time push to UI; reused as-is for Queue, History, Manga, and Chapter updates. |

## Already-Merged Divergences (on `Mangarr-v0`)

| File / Path | Type | Phase | Rationale |
|-------------|------|-------|-----------|
| `src/NzbDrone.Core/Tv/Series.cs` (lines 20-21, 29-30) | extend | pre-Phase-1 | Added `MalIds: HashSet<int>` and `AniListIds: HashSet<int>` to support manga cross-reference; collection type is `HashSet<int>` (not `List<int>` as some prior `.planning/codebase/` docs suggest). Kept on `Series` (not new `Manga` type) to defer rename to Phase 8 per leaf-first/rename-last sequence. |

## Planned Divergences (Not Yet on `Mangarr-v0`)

| File / Path | Type | Phase | Rationale |
|-------------|------|-------|-----------|
| `src/NzbDrone.Core/Datastore/Migration/000-…223-…` | delete | Phase 1 | Drop 224 inherited TV-shaped migrations; replace with manga baseline per [fresh-schema-confirmed.md](./.planning/decisions/fresh-schema-confirmed.md). |
| `src/NzbDrone.Core/Datastore/Migration/M001_baseline.cs` | new | Phase 1 | New manga baseline migration with composite indexes pre-built (per fresh-schema baseline floor). |
| `src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs` (BusyTimeout 1000→5000) | replace | Phase 1 | Per D-15 baseline floor; reduces SQLITE_BUSY surface area under v1 concurrency profile. |
| `src/NzbDrone.Core/MetadataSource/SkyHook/` | replace | Phase 2 | Replace TVDB/SkyHook with `IMetadataSource` plugin contract serving MangaDex / AniList / MAL. |
| `src/NzbDrone.Core/Parser/Parser.cs` (TV regex) | replace | Phase 2 | Replace TV-show regex with manga release-name regex (decimal chapter numbers, scanlation groups, `Ch.45-47` ranges). |
| `src/NzbDrone.Core/Indexers/Newznab/`, `Torznab/`, `Nyaa/`, `BroadcastheNet/`, `HDBits/`, etc. | delete | Phase 3 | TV/anime indexers; Mangarr's source model is aggregator websites, not Newznab/Torznab. |
| `src/NzbDrone.Core/Indexers/Mangadex/` (new) | new | Phase 3 | MangaDex bedrock indexer; Apache-2.0 attribution to `keiyoushi/extensions-source` if Mihon-derived per [source-onboarding-methodology.md](./.planning/decisions/source-onboarding-methodology.md). |
| `src/NzbDrone.Core/Indexers/Comix/` (new) | new | Phase 3 | comix.to reference port #1 per onboarding pipeline. |
| `src/NzbDrone.Core/Indexers/MangaFire/` (new) | new | Phase 3 | MangaFire reference port #2 per onboarding pipeline. |
| `src/NzbDrone.Core/Download/Clients/InProcess/` (new) | new | Phase 4 | Novel `InProcessImageDownloadClient` — no peer-fork precedent; bounded `Channel<T>` with resumable state. |
| `src/NzbDrone.Core/MediaFiles/Archive/` (new) | new | Phase 4 | `IChapterArchiver` strategy: CBZ + folder-of-images + ComicInfo.xml v2.0 / v2.1 dual-write. |
| `src/NzbDrone.Core/Qualities/Quality.cs` | delete | Phase 5 | TV resolutions enum; manga has no quality concept (replaced by Custom Formats + TranslationProfile per PROJECT.md Key Decision). |
| `src/NzbDrone.Core/Profiles/Translation/` (new — `TranslationProfile.cs`, `TranslationProfileService.cs`, `TranslationProfileRepository.cs`) | new | Phase 5 | Ordinal language-preference entity. Phase 5 scope expansion per [cf-only-walkthrough.md](./.planning/decisions/cf-only-walkthrough.md) verdict signoff (2026-05-01: TranslationProfile added). Applied as ordinal gate *before* Custom Format total-score in Decision Engine. |
| `src/NzbDrone.Core/Datastore/Migration/M002_translation_profile.cs` | new | Phase 5 | FluentMigrator migration for `TranslationProfile` entity + per-Manga FK. Required for TPROFILE-04 persistence. |
| `src/NzbDrone.Core/DecisionEngine/Specifications/` (TV-specific specs: `MonitoredEpisodeSpecification`, `MultiEpisodeSpecification`, `AnimeVersionUpgradeSpecification`, `RepackSpecification`, etc.) | replace | Phase 5 | TV-shaped specs replaced with manga equivalents; `IDownloadDecisionEngineSpecification` shape preserved. |
| `src/NzbDrone.Core/CustomFormats/` (architecture, not contents) | preserve | Phase 5 | CF infrastructure preserved; `LanguageSpecification` reusable as-is per the existing Sonarr pattern. |
| `src/Sonarr.Api.V5/` (Series/Episode controllers) | extend | Phase 7 | Add `/api/v5/manga`, `/chapter`, `/library` controllers alongside legacy Series/Episode controllers; rename in Phase 8. |
| `frontend/src/Series/`, `frontend/src/Episode/`, `frontend/src/AddSeries/` | extend | Phase 7 | Add parallel `Manga/`, `Chapter/`, `AddManga/` directories; rename and delete old in Phase 8. |
| All `Series → Manga`, `Episode → Chapter` C# rename | replace | Phase 8 | Domain rename deferred to Phase 8 cutover per leaf-first/rename-last; preserves upstream-merge ability through Phase 7. |
| `src/Sonarr.Api.V3/` (44 controllers) | (decision pending) | Phase 8 | Per PITFALLS.md #5 prevention strategy 5: V3 stays or is deleted in a single named commit. CONTEXT.md does not lock this; flagged here so the decision is not forgotten. |
| `THIRD-PARTY-NOTICES.md` | new | Phase 3 | Apache-2.0 attribution register; first entry = `keiyoushi/extensions-source` if any port draws from it. Mechanism committed in [source-onboarding-methodology.md](./.planning/decisions/source-onboarding-methodology.md). |
| `README.md` (rebrand + 3 dormant prior-art repos acknowledgement per BRAND-03) | replace | Phase 8 | Footer-style acknowledgement of `donderjoekel/Mangarr` (archived 2025-04-30), `hyminix/Mangarr`, `tnrd-org/Mangarr`. |
| `src/NzbDrone.Core/Indexers/Http/HttpAggregatorBase.cs` | extend | Phase 1 | New abstract base class extending `HttpIndexerBase<TSettings>` to inject per-`SourceKey` rate budget + honest-by-default User-Agent. Phase 3 source plugins (MangaDex/comix.to/MangaFire) extend this. Per D-10/D-11/D-12/D-13/D-14. |
| `src/NzbDrone.Core/Indexers/DownloadProtocol.cs` | extend | Phase 1 | New enum value `Http = 3` added (was `Unknown=0, Usenet=1, Torrent=2`). Required for in-process downloader (Phase 4). No `switch` statements use this enum exhaustively (verified via grep) — additive only. |
| `src/NzbDrone.Core/Datastore/Migration/*.cs` (224 files) → `001_mangarr_baseline.cs` | replace | Phase 1 | Single fresh-baseline migration replaces Sonarr's 224 inherited TV-shaped migrations (per D-14 Phase 0 + D-01 Phase 1 floor-only). Recreates Tv tables verbatim per D-02 for `Tv/` C# compile-compat (sit empty at runtime; deleted in Phase 8). Adds new manga tables (Manga, Chapters), nullable manga columns to History/Blocklist, and the five locked composite indexes from D-15. |
| `src/NzbDrone.Core/Datastore/BasicRepository.cs` | extend | Phase 1 | Polly `RetryStrategy.Execute(...)` wrapping extended from Insert/Update only (lines 214/414/426) to Find / Get / Get(IEnumerable) / Delete / DeleteMany / Upsert / Purge / Count / GetPagedRecordCount paths. Same `MaxRetryAttempts = 3` policy on `SQLITE_BUSY`. Per D-15 Phase 0 baseline floor. |
| `src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs` | extend | Phase 1 | SQLite `BusyTimeout` increased from `1000` to `5000` ms per D-15 Phase 0 baseline floor / SQLite docs recommendation for concurrent-write workloads. |

---
*Last updated: 2026-05-01 (Phase 0 — initial authoring)*
*Last updated: 2026-05-01 (Phase 0 — added TranslationProfile rows under Planned Divergences after cf-only-walkthrough verdict signoff: TranslationProfile added)*
*Last updated: 2026-05-01 (Phase 1 Plan 01 — appended 5 Phase 1 entries: HttpAggregatorBase, DownloadProtocol.Http, baseline migration replacement, Polly retry coverage extension, BusyTimeout 1000→5000)*
