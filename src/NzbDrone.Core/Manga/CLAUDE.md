# Manga (Domain Services)

## Purpose

Manga + Chapter domain models, services, repositories, and events. The leaf-most data substrate Phase 2 ships; Phase 3 indexers, Phase 4 archiver, Phase 5 decision engine, Phase 6 pipeline, and Phase 7 UI all consume these surfaces.

This directory is the **manga-side parallel** of `src/NzbDrone.Core/Tv/`. Both coexist throughout Phases 2-7 to preserve `v5-develop` upstream-merge ability. Phase 8 cutover deletes `Tv/` and renames `Manga/` to its canonical position.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Manga`

## Key Files

### Domain Models (Plan 02-02 deliverable)

| File | Purpose |
|------|---------|
| `Manga.cs` | Domain model (`ModelBase`). Singular cross-source IDs (`Guid? MangaDexId`, `int? MalId`, `int? AniListId`) — diverges from `Tv/Series.cs`'s pluralized `HashSet<int>` per 02-CONTEXT specifics (manga is 1:1 across sources, unlike anime). Carries D-21 multi-axis confirm inputs (`TotalChapterCount`, `PublicationYear`, `PrimaryAuthor`). |
| `Chapter.cs` | Domain model (`ModelBase, IComparable`). DECIMAL(10,3) `ChapterNumber` per Phase 2 D-12 widen, `ChapterType` enum, `IsSynthetic` D-17 marker, BCP-47 `TranslatedLanguage`. |

### Service Layer (Plan 02-03 deliverable — this plan)

| File | Purpose |
|------|---------|
| `IMangaService.cs` + `MangaService.cs` | CRUD + finders. Publishes `MangaAddedEvent` / `MangaUpdatedEvent` / `MangaDeletedEvent` on mutations. Constructor injects `IMangaRepository` + `IEventAggregator` + `Logger`. |
| `IChapterService.cs` + `ChapterService.cs` | CRUD + `FindByMangaAndNumber(int, decimal)` — Phase 16 STRUCT-01 dropped the `string translatedLanguage` arg (canonical Chapter is language-free). Per-language lookups go via `IChapterReleaseService.GetReleasesByChapter`. |

### Repository Layer (Plan 02-03 deliverable — this plan)

| File | Purpose |
|------|---------|
| `IMangaRepository.cs` + `MangaRepository.cs` | Dapper repo (`BasicRepository<Manga>`). Inherits Phase 1 D-15 Polly retry automatically. `FindByMangaDexId(Guid)` / `FindByMalId(int)` / `FindByAniListId(int)` — diverges from `Tv/SeriesRepository.FindByTvdbId` (manga has singular IDs). |
| `IChapterRepository.cs` + `ChapterRepository.cs` | Dapper repo (`BasicRepository<Chapter>`). `Find(int mangaId, decimal chapterNumber)` matches the Phase 16 STRUCT-01 (MangaId, ChapterNumber) UNIQUE key — language axis is gone (lifted to `ChapterRelease`). `GetSyntheticByMangaId` removed (STRUCT-03 — synthetic concept gone; `chapter.Releases.Count == 0` derives from D-04). |
| `ChapterRelease.cs` | NEW per-translation entity (Phase 16 STRUCT-02). Sibling of Chapter on the same `ModelBase` base. Soft FK on ChapterId; cascade in C# via `ChapterReleaseService.HandleAsync(MangaDeletedEvent)` per Pitfall 2 convention. |
| `IChapterReleaseRepository.cs` + `ChapterReleaseRepository.cs` | NEW Dapper repo (Phase 16 STRUCT-02). `Find(chapterId, lang, group)` natural-key query / `GetByChapterId(int)` / `GetByChapterIds(List<int>)` / `GetByMangaId(int)`. `GetByChapterIds` is the N+1-safe bulk path consumed by `IChapterReleaseService.GetReleasesByMangaId` for ChapterController per-Manga lookups. |
| `IChapterReleaseService.cs` + `ChapterReleaseService.cs` | NEW service (Phase 16 STRUCT-02). `GetReleasesByChapter` / `GetReleasesByMangaId` / `GetReleasesByChapterIds` / `DeleteByManga`; implements `IHandleAsync<MangaDeletedEvent>` for cascade-delete (mirrors `ChapterService.HandleAsync(MangaDeletedEvent)` at line 293-297). |

### Events (Plan 02-03 deliverable — this plan; under `Events/`)

| File | Purpose |
|------|---------|
| `Events/MangaAddedEvent.cs` | `IEvent` published after Insert. Mirrors `Tv/Events/SeriesAddedEvent.cs` verbatim shape. |
| `Events/MangaUpdatedEvent.cs` | `IEvent` published after Update (when `publishUpdatedEvent = true`). |
| `Events/MangaDeletedEvent.cs` | `IEvent` published after Delete. Carries `DeleteFiles` flag. |
| `Events/ChapterListUpdatedEvent.cs` | `IEvent` published by `RefreshMangaService` AFTER both passes of the Phase 16 STRUCT-07 two-pass (`EnsureChapter` + `SyncChapterReleases`) complete — Pitfall 4 single-emit invariant. Consumers re-read from `IChapterRepository` / `IChapterReleaseRepository` — the event does NOT carry the delta. |

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

## Phase 16 Invariants (post-2026-05-09 restructure)

- **Canonical Chapter grain:** `Chapter` is keyed on `(MangaId, ChapterNumber)` with UNIQUE-violation enforcement at SQL (STRUCT-01 — `IX_Chapters_MangaId_ChapterNumber`). NO per-language columns on Chapter. Per-language data lives on the new `ChapterRelease` sibling (STRUCT-02).
- **D-01: Stale-release retention.** `SyncChapterReleases` is upsert-on-natural-key — NEVER `DELETE missing`. Mirrors Sonarr's Episode-row retention. Re-running `RefreshMangaCommand` for the same manga never shrinks the ChapterRelease row count, only adds/updates.
- **D-02: FirstReleaseDate denormalization.** `Chapter.FirstReleaseDate` is the upstream chapter-publish date (Sonarr-mirror of `Episode.AirDateUtc`). DISTINCT from `ChapterRelease.ReleaseDate` (per-translation upload time supplied by indexer feed). The 4 historical callsites (`ChapterMonitoredService:80`, `ChapterRefreshedService:87-88`, `ShouldRefreshManga:72-76`, `RemoteChapter.IsRecentChapter()`) flipped from `chapter.ReleaseDate` to `chapter.FirstReleaseDate`.
- **D-04: Zero-release Chapter renders as Missing.** `EnsureChapter` always creates a real canonical Chapter row regardless of feed-match status. The placeholder-vs-real distinction that `IsSynthetic` previously carried is now derivable from `chapter.Releases.Count == 0`. Frontend `ChapterStatus.tsx` alias-flips Wanted/Missing pill on the same condition (NOT a 7th state — same precedence slot per Pitfall 5).
- **STRUCT-03: IsSynthetic concept removed entirely.** `grep -rn "IsSynthetic" src/ frontend/src/` returns 0 matches against the Chapter type. The flag is gone from schema, entity, resource, and frontend types.
- **Soft-FK convention (Pitfall 2).** `ChapterReleases.ChapterId` is a plain int column (NO `Create.ForeignKey` / `Rule.Cascade` in 001_mangarr_baseline.cs). Cascade-delete lives in C# via `ChapterReleaseService.HandleAsync(MangaDeletedEvent)`. Mirrors the existing `ChapterService.HandleAsync(MangaDeletedEvent)` pattern at `ChapterService.cs:293-297`.

## Manga Adaptation Notes

- **Phase 2 deliverable: storage substrate complete.** No pipeline, no archiver, no downloader (Phase 3+ territory).
- **Phase 8 cutover** will: rename `Manga/` → top-level (this directory becomes the canonical domain), delete `Tv/`. Until then, both directories coexist. ChapterRelease has NO TV peer; the sibling stays after Phase 8 collapse.
- **No `Volume` table**: `Chapter.VolumeNumber` is display-only metadata per PROJECT.md "Volumes / Seasons" Out-of-Scope row.
- **Synthesized chapter rows (HISTORICAL — superseded by Phase 16):** Pre-Phase-16, `Chapter.IsSynthetic` flag distinguished placeholder rows from real-feed rows (Phase 2 D-17). Phase 16 STRUCT-03 removed this concept entirely. Synthetic-ness is now derivable from `chapter.Releases.Count == 0` per D-04. The historical Phase 2 D-17 mechanism is gone.

## Cross-References

- **Schema**: `src/NzbDrone.Core/Datastore/Migration/001_mangarr_baseline.cs` (Phase 1 + Phase 2 deltas folded in 2026-05-02 per `.planning/decisions/dev-migration-policy.md`; Migration 002 was authored under the old append-only rule and has since been folded back into 001 + deleted).
- **Table mapping**: `src/NzbDrone.Core/Datastore/TableMapping.cs:139-140` registers `Manga` + `Chapter` entities. Dapper handles `Guid?` round-trips through the global `GuidConverter` registered for `Users.Identifier` (Phase 1 baseline).
- **Parser tree**: `src/NzbDrone.Core/Parser/Manga/CLAUDE.md` (parser side) — `MangaParsingService.Map` consumes `IChapterService.FindByMangaAndNumber` per D-03.
- **Metadata sources**: `src/NzbDrone.Core/MetadataSource/CLAUDE.md` (Plan 02-05+) — providers read/write Manga via `IMangaService`.
- **Mangarr analogs**: `src/NzbDrone.Core/Tv/CLAUDE.md` — domain-model precedents this directory mirrors.
- **Phase 10 Plan 10-07 SUMMARY** (3-arg `UpdateManga` overload + Option B controller opt-in): [`.planning/phases/10-events-and-subscribers-sweep/10-07-SUMMARY.md`](../../../.planning/phases/10-events-and-subscribers-sweep/10-07-SUMMARY.md).

---
*Last updated: 2026-05-02 after Plan 02-03 (services + repos + events landed).*
*Last edited: 2026-05-05 after Phase 10 Plan 10-09 close-out — added MangaService.UpdateManga overloads section reflecting Plan 10-07's 3-arg overload + Pitfall 4 ordering + Option B controller-PUT opt-in. Plan 10-07 ships the source code; Plan 10-09 ships this doc update per CLAUDE.md HIGH PRIORITY rule.*
*Last edited: 2026-05-09 after Phase 16 Plan 16-07a close-out — added ChapterRelease entity / repo / service triple to Key Files; added Phase 16 Invariants section (STRUCT-01 canonical grain + D-01 stale-release retention + D-02 FirstReleaseDate + D-04 zero-release Missing + STRUCT-03 IsSynthetic removal + Pitfall 2 soft-FK convention); revised Manga Adaptation Notes to mark Phase 2 D-17 IsSynthetic as historical (superseded by Phase 16 STRUCT-03).*
