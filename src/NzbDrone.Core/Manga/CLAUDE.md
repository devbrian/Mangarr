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
| `IChapterService.cs` + `ChapterService.cs` | CRUD + `FindByMangaAndNumber(int, decimal, string)` — D-03 contract consumed by `MangaParsingService.Map` (Plan 02-04). Slimmed vs. `Tv/EpisodeService` — no `IHandle<EpisodeFileDeletedEvent>`, no `IHandleAsync<SeriesScannedEvent>` (Phase 4+ territory). |

### Repository Layer (Plan 02-03 deliverable — this plan)

| File | Purpose |
|------|---------|
| `IMangaRepository.cs` + `MangaRepository.cs` | Dapper repo (`BasicRepository<Manga>`). Inherits Phase 1 D-15 Polly retry automatically. `FindByMangaDexId(Guid)` / `FindByMalId(int)` / `FindByAniListId(int)` — diverges from `Tv/SeriesRepository.FindByTvdbId` (manga has singular IDs). |
| `IChapterRepository.cs` + `ChapterRepository.cs` | Dapper repo (`BasicRepository<Chapter>`). `Find(mangaId, chapterNumber, translatedLanguage)` mirrors the Phase 1 composite-index key. `GetSyntheticByMangaId` surfaces D-17 placeholder rows for the Phase 3 indexer fill-in pipeline. |

### Events (Plan 02-03 deliverable — this plan; under `Events/`)

| File | Purpose |
|------|---------|
| `Events/MangaAddedEvent.cs` | `IEvent` published after Insert. Mirrors `Tv/Events/SeriesAddedEvent.cs` verbatim shape. |
| `Events/MangaUpdatedEvent.cs` | `IEvent` published after Update (when `publishUpdatedEvent = true`). |
| `Events/MangaDeletedEvent.cs` | `IEvent` published after Delete. Carries `DeleteFiles` flag. |
| `Events/ChapterListUpdatedEvent.cs` | `IEvent` published after `ChapterListService.SyncChapters` (Plan 02-09). Consumers re-read from `IChapterRepository` — the event does NOT carry the delta. |

## Patterns / Conventions

- **Mirror Sonarr's shape verbatim where it works.** `MangaService` mirrors `SeriesService.AddSeries` event-publish-after-insert at line 73-79. `ChapterRepository.Find` mirrors `EpisodeRepository.Find` at line 53-57.
- **Repositories extend `BasicRepository<T>`** (NOT `IRepository<T>` — that's a different abstraction). This inherits the static `RetryStrategy` Polly pipeline at `BasicRepository.cs:61-76` (D-15 Polly retry on `SQLITE_BUSY`).
- **Find methods are SINGULAR for cross-source IDs** (`FindByMangaDexId/MalId/AniListId`) because manga has 1:1 source mapping. Anime uses `HashSet<int> MalIds/AniListIds` because a single anime can carry multiple cross-references.
- **Constructor DI ordering** matches Sonarr precedent: `IRepository → IEventAggregator → IConfigService (optional) → ICached (optional) → Logger` last.
- **Event publishing happens in the SERVICE, not the repository.** Repositories raise low-level DB events via `IEventAggregator` (inherited from `BasicRepository`); services raise domain-level events (`MangaAddedEvent`, etc.).
- **DryIoc auto-discovery** picks up both services and both repositories via DI convention scanning. No manual registration required.

## Manga Adaptation Notes

- **Phase 2 deliverable: storage substrate complete.** No pipeline, no archiver, no downloader (Phase 3+ territory).
- **Phase 8 cutover** will: rename `Manga/` → top-level (this directory becomes the canonical domain), delete `Tv/`. Until then, both directories coexist.
- **No `Volume` table**: `Chapter.VolumeNumber` is display-only metadata per PROJECT.md "Volumes / Seasons" Out-of-Scope row.
- **Synthesized chapter rows**: `Chapter.IsSynthetic` flag distinguishes placeholder rows from real-feed rows. Plan 02-09's `ChapterListService` populates synthetic rows; Phase 3 indexers update them in place (flip `IsSynthetic = false` on real-feed match) per D-17.

## Cross-References

- **Schema**: `src/NzbDrone.Core/Datastore/Migration/001_mangarr_baseline.cs` (Phase 1 + Phase 2 deltas folded in 2026-05-02 per `.planning/decisions/dev-migration-policy.md`; Migration 002 was authored under the old append-only rule and has since been folded back into 001 + deleted).
- **Table mapping**: `src/NzbDrone.Core/Datastore/TableMapping.cs:139-140` registers `Manga` + `Chapter` entities. Dapper handles `Guid?` round-trips through the global `GuidConverter` registered for `Users.Identifier` (Phase 1 baseline).
- **Parser tree**: `src/NzbDrone.Core/Parser/Manga/CLAUDE.md` (parser side) — `MangaParsingService.Map` consumes `IChapterService.FindByMangaAndNumber` per D-03.
- **Metadata sources**: `src/NzbDrone.Core/MetadataSource/CLAUDE.md` (Plan 02-05+) — providers read/write Manga via `IMangaService`.
- **Sonarr analogs**: `src/NzbDrone.Core/Tv/CLAUDE.md` — domain-model precedents this directory mirrors.

---
*Last updated: 2026-05-02 after Plan 02-03 (services + repos + events landed).*
