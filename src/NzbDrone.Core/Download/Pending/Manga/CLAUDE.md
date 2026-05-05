# Download/Pending/Manga/

## Purpose

Manga sibling of `Download/Pending/` — holds `MangaDownloadDecision`s that need to wait
(delay profile, queue cooldown, indexer unavailability, RSS-sync fallback). On wake, the
service re-evaluates pending releases and grabs the highest-priority candidate.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Download\Pending\Manga`

## Key Files

| File | Purpose |
|------|---------|
| `MangaPendingRelease.cs` | POCO entity persisted to `MangaPendingReleases` table (Phase 9 D-09-06; Plan 09-09) |
| `IMangaPendingReleaseRepository.cs` + `MangaPendingReleaseRepository.cs` | Dapper CRUD via `BasicRepository<MangaPendingRelease>` (Plan 09-09) |
| `IMangaPendingReleaseService.cs` + `MangaPendingReleaseService.cs` | 9 public methods + 9 IHandle subscribers + Pitfall 4 publisher (Plan 09-10) |

## Patterns / Conventions

- **Pitfall 4 ordering**: every state-mutating path commits the DB write FIRST, publishes
  `MangaPendingReleasesUpdatedEvent` LAST. Enforced via the private `Insert(...)` and
  `Delete(...)` wrapper helpers in `MangaPendingReleaseService`. Mirrors TV
  `PendingReleaseService.cs:530-548` verbatim.
- **Static-list projection**: `_pendingReleases` is a static `List<MangaPendingRelease>`
  field rebuilt atomically via `UpdatePendingReleases()` (no lock — TV verbatim per
  PATTERNS section B; single-writer through the IHandle path makes the assignment safe).
- **Sibling-table BL-01 guard**: separate `Mapper.Entity<MangaPendingRelease>("MangaPendingReleases")`
  registration in `TableMapping.cs` makes it physically impossible to leak rows from
  the TV-side `PendingReleases` table even when MangaId/SeriesId int values collide.
  Mirrors `MangaBlocklistRepository.cs:11-15` precedent.
- **Namespace alias for `Manga` type**: this directory's namespace is `NzbDrone.Core.Download.Pending.Manga`
  so an unqualified `Manga` would resolve to the sub-namespace. Files that need the POCO
  type pin it with `using Manga = NzbDrone.Core.Manga.Manga;` (CS0118 mitigation per
  `IChapterFileService.cs:5` precedent).

## 9 IHandle Subscribers

| Event | Handler Behavior | Mutates State? |
|-------|------------------|----------------|
| `MangaEditedEvent` | Rebuild projection | No (pure rebuild) |
| `MangaUpdatedEvent` | Rebuild projection | No (pure rebuild) |
| `MangaDeletedEvent` | DeleteByMangaIds + rebuild + publish event | Yes |
| `ChapterGrabbedEvent` | RemoveGrabbed + rebuild | Yes (via Delete wrapper) |
| `MangaRssSyncCompleteEvent` | RemoveRejected (filter `.Rejected`) + rebuild | Yes (via Delete wrapper) |
| `CustomFormatProfileUpdatedEvent` | Rebuild projection | No |
| `TranslationProfileUpdatedEvent` | Rebuild projection (9th — Open Q §1) | No |
| `ConfigSavedEvent` | Rebuild projection | No |
| `ApplicationStartedEvent` | Rebuild projection (warm cache on boot) | No |

**`MangaRssSyncCompleteEvent`**, NOT TV's `RssSyncCompleteEvent` — Plan 09-12 audit
gap-01 close-out introduced the manga sibling because `MangaRssSyncService` never
publishes the TV event AND the payload shape differs (flat `List<MangaDownloadDecision>`
vs TV's tri-split `ProcessedDecisions`).

## 9 Public Methods (3 TV `*Obsolete` skipped per D-09-06 — manga has no V3 API)

- `Add(MangaDownloadDecision, PendingReleaseReason)`
- `AddMany(List<Tuple<MangaDownloadDecision, PendingReleaseReason>>)`
- `GetPending() → List<ReleaseInfo>`
- `GetPendingRemoteChapters(int mangaId) → List<RemoteChapter>`
- `GetPendingQueue() → List<MangaQueueItem>`
- `FindPendingQueueItem(int queueId) → MangaQueueItem`
- `RemovePendingQueueItems(int queueId)`
- `OldestPendingRelease(int mangaId, int[] chapterIds) → RemoteChapter`

## Manga Adaptation Notes

- **No QualityModelComparer** in `RemoveGrabbed` or `GetPendingQueue` dedup (Phase 5 D-04
  — manga has no QualityProfile). Dedup keys off chapter-id-set only; quality axis from
  the TV implementation drops out cleanly. CustomFormat score axis is re-evaluated on
  the next RSS sync, not at the pending-release pruning step.
- **`MangaDeletedEvent.Manga` is singular** (not a list like TV `SeriesDeletedEvent.Series`)
  — `Handle(MangaDeletedEvent)` wraps `message.Manga.Id` as a single-element list for
  the repo accessor.
- **`ChapterGrabbedEvent.RemoteChapter`** is the property name (not `.Chapter`) — payload
  carries the full `RemoteChapter` (with `.Chapters` list and `.Manga`), not a single
  Chapter row.
- **`Lazy<DateTime> nextRssSync`** uses `MangaRssSyncCommand` (not TV `RssSyncCommand`)
  for the manga-specific RSS-sync schedule. Pulled via `_taskManager.GetNextExecution(typeof(MangaRssSyncCommand))`.
- **`_configService.MangaRssSyncInterval`** for the per-rebuild cooldown step (parallel
  to TV's `RssSyncInterval`); manga config key landed in Phase 6 per `IConfigService.cs:122`.

## Known Limitations

- **`DelayProfile.GetProtocolDelay(DownloadProtocol.Http)` returns `UsenetDelay`** (Open Q §3,
  RESEARCH Pitfall 5). Manga delay-profile cooldown silently uses the Usenet number.
  `GetDelay(RemoteChapter)` carries the inline `// KNOWN LIMITATION` comment + `// TODO v1.1`
  pointer. Deferred to v1.1 (`MangaDelayProfile` follow-up logged in Plan 09-11 close-out).

## Anti-Patterns

- **DO NOT subscribe** `AutoRetryOrchestrator` to `MangaPendingReleasesUpdatedEvent`
  (anti-race contract per Plan 06-08 D-12/D-13 + RESEARCH Open Q §2). Auto-retry already
  consumes `MangaBlocklistAddedEvent`; double-subscription would create an infinite
  re-search loop where every pending-release rebuild triggers a fresh search that
  re-adds pending releases.
- **DO NOT subscribe** to `MangaBlocklistAddedEvent` or `ChapterDownloadFailedEvent`
  from this service — both are owned by `AutoRetryOrchestrator`. Route through
  `ChapterGrabbedEvent` (success path) and `MangaRssSyncCompleteEvent` (rejected path).

## Phase 14 Cleanup

Collapse with `Download/Pending/` (TV side) when `Tv/` deletes. The 3 `*Obsolete` TV
methods drop entirely (no manga V3 API ever existed). The TV / manga `*PendingReleasesUpdatedEvent`
pair collapses to one event published by the unified service.

## Cross-References

- TV analog: [`src/NzbDrone.Core/Download/Pending/`](../) (`PendingReleaseService.cs`, 701 lines)
- Plan 06-08 [`AutoRetryOrchestrator`](../../Manga/AutoRetryOrchestrator.cs) — anti-race contract
- [`MangaPendingReleasesUpdatedEvent`](../../../Queue/Manga/MangaQueueUpdatedEvent.cs) at line 24 — empty-shell event this service publishes
- [`SignalRListener.tsx`](../../../../../frontend/src/Components/SignalRListener.tsx) lines 441-447 (`manga/queue` SignalR consumer — no frontend change needed)
- Plan 09-12 [`MangaRssSyncCompleteEvent`](../../../IndexerSearch/Manga/MangaRssSyncCompleteEvent.cs) — manga-shape sibling event consumed here
