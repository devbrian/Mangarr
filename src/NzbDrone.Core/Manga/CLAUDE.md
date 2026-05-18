# Manga (Domain Services)

## Purpose

Manga + Chapter domain models, services, repositories, and events. The leaf-most data substrate Phase 2 ships; Phase 3 indexers, Phase 4 archiver, Phase 5 decision engine, Phase 6 pipeline, and Phase 7 UI all consume these surfaces.

This directory is the **manga-side parallel** of `src/NzbDrone.Core/Tv/`. Both coexist throughout Phases 2-7 to preserve `v5-develop` upstream-merge ability. Phase 8 cutover deletes `Tv/` and renames `Manga/` to its canonical position.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Manga`

## Key Files

### Domain Models (Plan 02-02 deliverable)

| File | Purpose |
|------|---------|
| `Manga.cs` | Domain model (`ModelBase`). Singular cross-source IDs (`Guid? MangaDexId`, `int? MalId`, `int? AniListId`) — diverges from `Tv/Series.cs`'s pluralized `HashSet<int>` per 02-CONTEXT specifics (manga is 1:1 across sources, unlike anime). Carries D-21 multi-axis confirm inputs (`TotalChapterCount`, `PublicationYear`, `PrimaryAuthor`). **Phase 24 additions (Plan 24-02, Migration 002):** `string Artist` (line 116 — populated from MangaDex `relationships[type=artist].attributes.name` per Open Q #6 atomic-fix; AniList/MAL fall back to null per Phase 27 territory) + `MangaDemographic? Demographic` (line 117 — backs `DemographicSpecification` per AT-04; null = "not categorized"; populated from MangaDex `attributes.publicationDemographic`). |
| `Chapter.cs` | Domain model (`ModelBase, IComparable`). DECIMAL(10,3) `ChapterNumber` per Phase 2 D-12 widen, `ChapterType` enum, `Monitored` flag, `FirstReleaseDate?` (Sonarr-mirror of `Episode.AirDateUtc`). Phase 16.1 STRUCT-01 canonical key is `(MangaId, ChapterNumber)` — language-free per Sonarr-mirror; per-translation axes live on `ChapterFile.TranslatedLanguage` + `ChapterFile.ScanlationGroup` (Phase 6 PIPELINE-04). |
| `MangaDemographic.cs` | **NEW (Phase 24 Plan 24-02, D-04)** — int-backed enum with 4 values: `Shonen=1, Shojo=2, Seinen=3, Josei=4`. Backs the new `Manga.Demographic` column added by Migration 002. Null (no explicit `None=0` sentinel) = "not categorized" — semantically cleaner than Sonarr's `Unknown=0` convention for manga where many titles lack demographic categorization. MangaDex `publicationDemographic` 4-value enum maps 1:1; AniList/MAL fall back to null (Phase 27 follow-up). SelectOptions source for `DemographicSpecification`. See [DIVERGENCE.md § Phase 24 — Entry 2](../../../DIVERGENCE.md). |
| `MangaContentRating.cs` | **NEW (Phase 24 Plan 24-03)** — int-backed enum sentinel mirror with 4 values: `Safe=1, Suggestive=2, Erotica=3, Pornographic=4`. The `Manga.ContentRating` entity field itself stays `string` (already shipped pre-Phase-24; no Migration 002 column add); the enum is FE-facing only for `ContentRatingSpecification.SelectOptions` dropdown UX. The spec's `IsSatisfiedByWithoutNegate` helper maps stored string → enum at evaluation time. Mirrors MangaDex's `contentRating` field 4-value enum. Pitfall 1 reshape Option A pattern. |
| `MangaStatus.cs` | **NEW (Phase 24 Plan 24-03 — Pitfall 1 reshape Option A)** — int-backed enum sentinel mirror for `StatusSpecification.SelectOptions` (Mangarr-canonical `MangaStatusType` is a static class of string constants, not an int-backed enum as 24-PATTERNS line 768 originally assumed). Same Option-A approach as MangaContentRating: the entity field stays string; the enum is the spec's SelectOptions source. |

### Service Layer (Plan 02-03 deliverable — this plan)

| File | Purpose |
|------|---------|
| `IMangaService.cs` + `MangaService.cs` | CRUD + finders. Publishes `MangaAddedEvent` / `MangaUpdatedEvent` / `MangaDeletedEvent` on mutations. Constructor injects `IMangaRepository` + `IEventAggregator` + `Logger`. |
| `IChapterService.cs` + `ChapterService.cs` | CRUD + `FindByMangaAndNumber(int, decimal)` — Phase 16 STRUCT-01 dropped the `string translatedLanguage` arg (canonical Chapter is language-free). Per-translation lookups go via `IChapterFileService.GetFilesByChapter` reading `ChapterFile.TranslatedLanguage` + `ChapterFile.ScanlationGroup` (Phase 16.1 — the per-translation axes live on the file entity, mirroring Sonarr's `EpisodeFile.Languages` placement). |

### Repository Layer (Plan 02-03 deliverable — this plan)

| File | Purpose |
|------|---------|
| `IMangaRepository.cs` + `MangaRepository.cs` | Dapper repo (`BasicRepository<Manga>`). Inherits Phase 1 D-15 Polly retry automatically. `FindByMangaDexId(Guid)` / `FindByMalId(int)` / `FindByAniListId(int)` — diverges from `Tv/SeriesRepository.FindByTvdbId` (manga has singular IDs). |
| `IChapterRepository.cs` + `ChapterRepository.cs` | Dapper repo (`BasicRepository<Chapter>`). `Find(int mangaId, decimal chapterNumber)` matches the Phase 16 STRUCT-01 (MangaId, ChapterNumber) UNIQUE key — language axis is gone (per-translation axes now live on `ChapterFile.TranslatedLanguage` + `ChapterFile.ScanlationGroup` per Phase 16.1 — Sonarr-canonical pattern). `GetSyntheticByMangaId` removed (Phase 16 STRUCT-03 — synthetic concept gone; synthetic-ness derives from `monitored && ChapterFileId == null` per Phase 16.1 Sonarr-canonical Wanted/Missing predicate). |

### Events (Plan 02-03 deliverable — this plan; under `Events/`)

| File | Purpose |
|------|---------|
| `Events/MangaAddedEvent.cs` | `IEvent` published after Insert. Mirrors `Tv/Events/SeriesAddedEvent.cs` verbatim shape. |
| `Events/MangaUpdatedEvent.cs` | `IEvent` published after Update (when `publishUpdatedEvent = true`). |
| `Events/MangaDeletedEvent.cs` | `IEvent` published after Delete. Carries `DeleteFiles` flag. |
| `Events/ChapterListUpdatedEvent.cs` | `IEvent` published by `RefreshMangaService` AFTER `IChapterListService.SyncChapters` (Phase 16.1 single-pass; mirrors Sonarr's `IRefreshEpisodeService.RefreshEpisodeInfo`) completes — Pitfall 4 single-emit invariant: DB-write FIRST, event LAST. Consumers re-read from `IChapterRepository` — the event does NOT carry the delta. |

### Move Manga slice (Phase 2 Plan 02-16 + issue #81 wire-up)

| File | Purpose |
|------|---------|
| `Commands/MoveMangaCommand.cs` | Single-manga move payload (`MangaId`, `SourcePath`, `DestinationPath`). `SendUpdatesToClient=true`, `RequiresDiskAccess=true`. Mirrors upstream `Tv/Commands/MoveSeriesCommand.cs` verbatim. |
| `Commands/BulkMoveMangaCommand.cs` | Multi-manga move payload (`Manga: List<BulkMoveManga>`, `DestinationRootFolder`). `BulkMoveManga` is `IEquatable<>` keyed on `MangaId` for dedup. Mirrors upstream `Tv/Commands/BulkMoveSeriesCommand.cs` verbatim. |
| `MoveMangaService.cs` | `IExecute<MoveMangaCommand>` + `IExecute<BulkMoveMangaCommand>` handler. DryIoc auto-discovered (no DI registration). Uses `IDiskTransferService.TransferFolder(TransferMode.Move)` — cross-drive falls back to copy+verify+delete automatically. Idempotency: `sourcePath.PathEquals(destinationPath)` short-circuit at line 68-72 logs "is already in the specified location" and returns without file ops. On `IOException`, `RevertPath` restores `manga.Path` via `_mangaService.UpdateManga(manga)` (2-arg overload → `MangaUpdatedEvent` only). Mirrors upstream `Tv/MoveSeriesService.cs` verbatim with manga type swaps. |
| `Events/MangaMovedEvent.cs` | `IEvent` published after successful TransferFolder. Carries `Manga` + `SourcePath` + `DestinationPath`. Mirrors upstream `Tv/Events/SeriesMovedEvent.cs`. |

**Phase 2 → issue #81 status:** The slice shipped Phase 2 Plan 02-16 but had NO controller publish site until issue #81 wired the V5 controllers (`MangaController.UpdateManga` for single, `MangaEditorController.SaveAll` for bulk). Pre-issue-81 the slice was 90% built / 0% reachable — frontend `useSaveManga` sent `?moveFiles=true` but the backend silently ignored it. Test fixture lives at `src/NzbDrone.Core.Test/Manga/MoveMangaServiceFixture.cs` (6 tests: happy path + revert + idempotency + bulk-path + skip-missing-folder; 1:1 port of upstream `MoveSeriesServiceFixture` + 1 Mangarr-specific idempotency test).


## Patterns / Conventions

- **Mirror Mangarr's shape verbatim where it works.** `MangaService` mirrors `SeriesService.AddSeries` event-publish-after-insert at line 73-79. `ChapterRepository.Find` mirrors `EpisodeRepository.Find` at line 53-57.
- **Repositories extend `BasicRepository<T>`** (NOT `IRepository<T>` — that's a different abstraction). This inherits the static `RetryStrategy` Polly pipeline at `BasicRepository.cs:61-76` (D-15 Polly retry on `SQLITE_BUSY`).
- **Find methods are SINGULAR for cross-source IDs** (`FindByMangaDexId/MalId/AniListId`) because manga has 1:1 source mapping. Anime uses `HashSet<int> MalIds/AniListIds` because a single anime can carry multiple cross-references.
- **Constructor DI ordering** matches Mangarr precedent: `IRepository → IEventAggregator → IConfigService (optional) → ICached (optional) → Logger` last.
- **Event publishing happens in the SERVICE, not the repository.** Repositories raise low-level DB events via `IEventAggregator` (inherited from `BasicRepository`); services raise domain-level events (`MangaAddedEvent`, etc.).
- **DryIoc auto-discovery** picks up both services and both repositories via DI convention scanning. No manual registration required.

## MangaService.UpdateManga overloads (post-Phase-10)

**Phase 10 Plan 10-07** (FINDINGS Open Q 5 close-out) added a 3-arg overload alongside the existing 2-arg overload. Both are declared on `IMangaService` and implemented on `MangaService`. The 2-arg overload is preserved for backwards compatibility and now delegates to the 3-arg overload.

| Signature | Use case | Events published |
|-----------|----------|------------------|
| `UpdateManga(Manga manga, bool publishUpdatedEvent = true)` (2-arg) | Existing callers — `RefreshMangaService` passes `publishUpdatedEvent: false` to suppress events on the metadata-refresh path; `MoveMangaService` and `MangaLinksController` default to `true`. | Body delegates to 3-arg overload with `triggerSeriesEdited: false`. Publishes only `MangaUpdatedEvent` (when flag true). |
| `UpdateManga(Manga manga, bool publishUpdatedEvent, bool triggerSeriesEdited)` (3-arg, **NEW Plan 10-07**) | UI single-edit `MangaController` PUT path passes both flags `true` so `MangaController.IHandle<MangaEditedEvent>` (Plan 10-05) fires on the user-explicit-edit signal AND `IHandle<MangaUpdatedEvent>` fires on the any-update signal atomically. `RefreshMangaService` does not call this overload — it goes through the 2-arg form with `publishUpdatedEvent: false` to suppress both events. | Both `MangaUpdatedEvent` AND `MangaEditedEvent`, each independently gated by its respective bool. |

**Pitfall 4 ordering invariant** (10-PATTERNS section B; numbered inline comments lock the contract): **DB Update FIRST, `MangaUpdatedEvent` SECOND, `MangaEditedEvent` LAST.** Subscribers reading manga state from `IMangaRepository` in response to either event see committed values. The two-event ordering is novel — TV's analog (`Tv/SeriesService.UpdateSeries`) publishes only one event, so the question doesn't arise upstream.

**TV semantic mirror (with explicit divergence):** `publishUpdatedEvent` gates the "any update including metadata refresh" signal; `triggerSeriesEdited` gates the "user-explicit edit only" signal. The parameter name `triggerSeriesEdited` preserves TV verbatim per PROJECT.md design philosophy (*preserve Mangarr's shape; diverge only where the manga domain forces us*); Phase 14 will rename it to `triggerMangaEdited` when `Tv/` deletes. Mangarr divergence note: TV's actual `UpdateSeries(Series, bool, bool)` third arg is `updateEpisodesToMatchSeason` (a season-fan-out gate, NOT a second event-publish gate); manga's 3-arg overload introduces the dual-publish pattern explicitly because the controller-PUT path needs to opt into both events without forking the controller call site. Documented in the per-method comment block on the 3-arg method.

**BL-09 fix preservation:** Both overloads route through the 3-arg method's Find-first existence guard. If `_mangaRepository.Find(manga.Id)` returns null, the method throws `ModelNotFoundException` BEFORE Dapper's UPDATE silently no-ops on the missing row. Without this guard, SignalR would broadcast a phantom event the UI then re-fetches and 404s on. Tests cover the throw path explicitly.

**Existing bulk-edit path (UNCHANGED by Plan 10-07):** `MangaService.UpdateManga(List<Manga>, bool useExistingRelativeFolder)` (Phase 8 audit gap-01 close-out) publishes `MangaBulkEditedEvent` independently of the single-item overloads. Pre-Plan-10-07, the 2-arg single-item overload was the ONLY publisher of `MangaEditedEvent` (Phase 8 audit gap-09 close-out behavior). After Plan 10-07, the 2-arg path emits `MangaUpdatedEvent` instead, and the controller-PUT 3-arg path is the canonical publisher of `MangaEditedEvent`. SignalR consumer outcome is unchanged — `MangaController.IHandle<MangaEditedEvent>` and `IHandle<MangaUpdatedEvent>` both broadcast `BroadcastResourceChange(ModelAction.Updated, manga.Id)`. Behavioral migration: `MoveMangaService` and `MangaLinksController` 2-arg callers now publish `MangaUpdatedEvent` (was `MangaEditedEvent`); semantic intent is now tightened — `MangaEditedEvent` truly means "user explicitly hit Save in the manga edit form".

**Test coverage:** `src/NzbDrone.Core.Test/Manga/MangaServiceFixture.cs` (Plan 10-07 introduced — 6 NUnit tests covering the (`publishUpdatedEvent`, `triggerSeriesEdited`) matrix + 2-arg delegation contract + BL-09 `ModelNotFoundException` + `ArgumentNullException` paths). All 6 green; full `MangaTests` namespace 47/47 green; full solution build clean.

**Caller-side opt-in status — Option B locked:** Plan 10-07 chose **Option B** (per W4 revision iteration 1; D-10-08 audit). `MangaController` PUT (`src/Mangarr.Api.V5/Manga/MangaController.cs:144`) calls the 3-arg overload directly with `publishUpdatedEvent: true, triggerSeriesEdited: true`. The legacy 2-arg call site `_mangaService.UpdateManga(existing)` was removed from the controller — verified by `grep` against the file. Plan 10-05's `IHandle<MangaEditedEvent>` consumer is therefore wired end-to-end on the UI single-edit path on first deploy of the Plan 10-07 commits; no follow-up plan needed to flip the switch.

## Phase 16.1 Invariants (post-2026-05-10 revert; Sonarr-canonical pattern)

Phase 16.1 reverted Phase 16's `ChapterRelease` sibling table in favor of routing per-translation axes through the existing `ChapterFile` translation-axis fields (Phase 6 PIPELINE-04). The invariants below describe the post-revert canonical shape.

- **Canonical Chapter grain:** `Chapter` is keyed on `(MangaId, ChapterNumber)` with UNIQUE-violation enforcement at SQL (Phase 16 STRUCT-01 — `IX_Chapters_MangaId_ChapterNumber`; Sonarr-mirror of `(SeriesId, SeasonNumber, EpisodeNumber)` UNIQUE). The canonical Chapter row is **language-free**; per-translation data lives on `ChapterFile.TranslatedLanguage` + `ChapterFile.ScanlationGroup` (Phase 16.1 — Sonarr-canonical pattern; mirrors `EpisodeFile.Languages` placement).
- **`ChapterListService.SyncChapters` is single-pass (Phase 16.1 REVERT-03).** Single canonical Sync method; mirrors Sonarr's `IRefreshEpisodeService.RefreshEpisodeInfo`. The Phase 16 two-pass split (`EnsureChapter` + `SyncChapterReleases`) is gone. `RefreshMangaService` calls `_chapterListService.SyncChapters(manga, remoteChapters)` once per refresh.
- **Stale-chapter handling LOCKED: `SyncChapters` does NOT delete stale chapters** (Phase 16.1 CONTEXT.md Pitfall #4 + PATTERNS.md Pitfall 6). Pre-Phase-16 behavior retained. Sonarr's `RefreshEpisodeService` DOES delete stale Episode rows, but Phase 16.1 SPEC scope is "revert," not "Sonarr-canonicalize stale-handling." Test pin: `SyncChapters_does_not_DELETE_stale_chapters` in `ChapterListServiceFixture`.
- **`Chapter.FirstReleaseDate` survives the revert** (Phase 16.1 SPEC: "Out of scope: Phase 16 D-02 — `Chapter.FirstReleaseDate` survives"). Denormalized stored column — Sonarr-mirror of `Episode.AirDateUtc`. Populated by `MangaDexMetadataSource.MapChapter` from `chapter.publishedAt`. The 4 historical callsites (`ChapterMonitoredService:80`, `ChapterRefreshedService:87-88`, `ShouldRefreshManga:72-76`, `RemoteChapter.IsRecentChapter()`) consume `chapter.FirstReleaseDate`.
- **Wanted/Missing semantic is Sonarr-canonical** (Phase 16.1 REVERT-05): `monitored && ChapterFileId == null` mirrors `Episode.Monitored && EpisodeFileId == 0` byte-for-byte. The Phase 16 D-04 alias-flip (`releases.length === 0`) is gone. The pre-Phase-16-`IsSynthetic` placeholder concept is also gone — synthetic-ness derives from no-file, not from a flag.
- **Pattern 4 cascade preserved across the revert.** `ChapterService.HandleAsync(MangaDeletedEvent)` at `ChapterService.cs:293-297` bulk-deletes Chapter rows by mangaId on Manga delete (soft-FK convention; no `Create.ForeignKey` / `Rule.Cascade` in 001_mangarr_baseline.cs). The Phase 16 `ChapterReleaseService.HandleAsync` cascade hop is gone — same privilege boundary, narrower graph.

**Historical note:** Phase 16 (closed 2026-05-09) introduced a `ChapterRelease` sibling persistent layer that carried per-(language, scanlation-group) translation metadata. Phase 16.1 reverted that approach in favor of the Sonarr-canonical pattern (per-translation axes on `ChapterFile`). The 5 production files (`ChapterRelease.cs` + `IChapterReleaseRepository`/Repository + `IChapterReleaseService`/Service) and 2 test fixtures were deleted in Wave 4a. See `.planning/phases/16.1-revert-chapterrelease-adopt-sonarr-canonical-translation-pat/16.1-SUMMARY.md` for the full record.

## Manga Adaptation Notes

- **Phase 2 deliverable: storage substrate complete.** No pipeline, no archiver, no downloader (Phase 3+ territory).
- **Phase 8 cutover** will: rename `Manga/` → top-level (this directory becomes the canonical domain), delete `Tv/`. Until then, both directories coexist. (Phase 15 cutover delivered most of this work; the residual TV→Manga rename for the directory itself remains.)
- **No `Volume` table**: `Chapter.VolumeNumber` is display-only metadata per PROJECT.md "Volumes / Seasons" Out-of-Scope row.
- **Synthesized chapter rows (HISTORICAL — superseded by Phase 16 + Phase 16.1):** Pre-Phase-16, `Chapter.IsSynthetic` flag distinguished placeholder rows from real-feed rows (Phase 2 D-17). Phase 16 STRUCT-03 removed the flag. Phase 16 D-04 then re-derived synthetic-ness from `chapter.Releases.Count == 0`. Phase 16.1 reverts D-04 (Sonarr-canonical: synthetic-ness is `monitored && ChapterFileId == null`, mirrors `Episode.Monitored && EpisodeFileId == 0`). Net effect: `IsSynthetic` is gone in all forms; the canonical Wanted/Missing predicate matches Sonarr exactly.

## Cross-References

- **Schema**: `src/NzbDrone.Core/Datastore/Migration/001_mangarr_baseline.cs` (Phase 1 + Phase 2 deltas folded in 2026-05-02 per `.planning/decisions/dev-migration-policy.md`; Migration 002 was authored under the old append-only rule and has since been folded back into 001 + deleted).
- **Table mapping**: `src/NzbDrone.Core/Datastore/TableMapping.cs` registers `Manga` + `Chapter` entities (the Phase 16 `ChapterRelease` registration was removed in Phase 16.1 Wave 4a). Dapper handles `Guid?` round-trips through the global `GuidConverter` registered for `Users.Identifier` (Phase 1 baseline).
- **Parser tree**: `src/NzbDrone.Core/Parser/Manga/CLAUDE.md` (parser side) — `MangaParsingService.Map` consumes `IChapterService.FindByMangaAndNumber` per D-03.
- **Metadata sources**: `src/NzbDrone.Core/MetadataSource/CLAUDE.md` (Plan 02-05+) — providers read/write Manga via `IMangaService`.
- **Mangarr analogs**: `src/NzbDrone.Core/Tv/CLAUDE.md` — domain-model precedents this directory mirrors.
- **Phase 10 Plan 10-07 SUMMARY** (3-arg `UpdateManga` overload + Option B controller opt-in): [`.planning/phases/10-events-and-subscribers-sweep/10-07-SUMMARY.md`](../../../.planning/phases/10-events-and-subscribers-sweep/10-07-SUMMARY.md).

---
*Last updated: 2026-05-02 after Plan 02-03 (services + repos + events landed).*
*Last edited: 2026-05-05 after Phase 10 Plan 10-09 close-out — added MangaService.UpdateManga overloads section reflecting Plan 10-07's 3-arg overload + Pitfall 4 ordering + Option B controller-PUT opt-in. Plan 10-07 ships the source code; Plan 10-09 ships this doc update per CLAUDE.md HIGH PRIORITY rule.*
*Last edited: 2026-05-09 after Phase 16 Plan 16-07a close-out — added ChapterRelease entity / repo / service triple to Key Files; added Phase 16 Invariants section (STRUCT-01 canonical grain + D-01 stale-release retention + D-02 FirstReleaseDate + D-04 zero-release Missing + STRUCT-03 IsSynthetic removal + Pitfall 2 soft-FK convention); revised Manga Adaptation Notes to mark Phase 2 D-17 IsSynthetic as historical (superseded by Phase 16 STRUCT-03).*
*Last edited: 2026-05-10 after Phase 16.1 Plan 16.1-05 close-out — REVERT pass: removed `ChapterRelease` entity / repo / service triple from Key Files (entity layer deleted in Wave 4a per Phase 16.1 REVERT-01); replaced "Phase 16 Invariants" section with "Phase 16.1 Invariants" (single-pass `SyncChapters` + Sonarr-canonical Wanted/Missing predicate `monitored && ChapterFileId == null` + `ChapterFile.TranslatedLanguage` + `ChapterFile.ScanlationGroup` as canonical per-translation axes per Phase 16.1 D-04 / D-06 / REVERT-05 / REVERT-06); revised Manga Adaptation Notes to capture the full IsSynthetic-→-zero-release-→-Sonarr-canonical history. Added "Historical note" anchor pointing at 16.1-SUMMARY.md.*
