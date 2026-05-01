# Activity/

## Purpose

The "Activity" section of the app — three views into download lifecycle:

- **Queue** — currently in-flight downloads
- **History** — completed grabs and imports (success and failure)
- **Blocklist** — releases that failed and shouldn't be retried

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Activity\`

## Subdirectories

### Queue/ (~20 files)
Live view of active downloads from the configured download clients.

| File | Purpose |
|------|---------|
| `Queue.tsx` | Page |
| `QueueRow.tsx` | One queue item |
| `QueueDetails.tsx`, `Details/QueueDetailsProvider.tsx` | Details inline / modal |
| `QueueStatus.tsx`, `QueueStatusCell.tsx` | Status badge |
| `EpisodeCellContent.tsx`, `EpisodeTitleCellContent.tsx` | Per-episode rendering |
| `ProtocolLabel.tsx` | Usenet/Torrent badge |
| `QueueFilterModal.tsx` | Filter |
| `queueOptionsStore.ts` | Zustand options |
| `RemoveQueueItemModal.tsx` | Remove + retry / blocklist confirmation |

### History/ (~20 files)
Persistent log of every grab/import event.

| File | Purpose |
|------|---------|
| `History.tsx` | Page (server-side paged) |
| `HistoryRow.tsx` | One row |
| `HistoryEventTypeCell.tsx` | Event type badge (Grabbed/Imported/Failed/Deleted/Renamed) |
| `Details/HistoryDetailsModal.tsx` | Detail modal |
| `HistoryFilterModal.tsx` | Filter |
| `historyOptionsStore.ts` | Zustand options |
| `useHistory.ts` | Generic history hook |
| `useEpisodeHistory.ts` | Per-episode history |
| `useSeriesHistory.ts` | Per-series history |

### Blocklist/ (~5 files)
Releases marked as "don't retry."

| File | Purpose |
|------|---------|
| `Blocklist.tsx` | Page |
| `BlocklistRow.tsx` | Row |
| `BlocklistDetailsModal.tsx` | Details |
| `BlocklistFilterModal.tsx` | Filter |
| `blocklistOptionsStore.ts` | Zustand |
| `useBlocklist.ts` | Hook |

## Server-Side Pagination

History/Queue/Blocklist are all server-paged (large data sets). They use `createServerSideCollectionHandlers` from Redux store creators rather than client-side filter/sort.

## Real-Time Updates

The SignalR `queue` and `history` messages keep these views fresh without polling. `Queue` is particularly interactive — progress bars update live.

## Manga Adaptation Notes

These views are **largely reusable**. Migration is mostly terminology:

| Sonarr term in UI | Manga term |
|-------------------|------------|
| Episode | Chapter |
| Season | Volume |
| "Episode title" column | "Chapter title" column |
| `EpisodeCellContent`, `EpisodeTitleCellContent` | Rename to `ChapterCellContent`, `ChapterTitleCellContent` |

The data shapes are minimally different (fields like `episodeId` become `chapterId`).

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../../../src/NzbDrone.Core/Queue/](../../../src/NzbDrone.Core/Queue/) — Backend Queue
- [../../../src/NzbDrone.Core/History/](../../../src/NzbDrone.Core/History/) — Backend History
- [../../../src/NzbDrone.Core/Blocklisting/](../../../src/NzbDrone.Core/Blocklisting/) — Backend Blocklist
- [../../../src/Sonarr.Api.V5/Queue/](../../../src/Sonarr.Api.V5/Queue/) — REST endpoints
