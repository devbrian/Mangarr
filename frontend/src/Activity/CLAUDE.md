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
| `Queue.tsx` | Page (accepts `mediaType?: 'series' \| 'manga'` prop — Phase 7 D-10) |
| `MangaQueue.tsx` | Phase 7 thin wrapper rendering `<Queue mediaType="manga" />` (Plan 07-09) |
| `QueueRow.tsx` | One queue item |
| `QueueDetails.tsx`, `Details/QueueDetailsProvider.tsx` | Details inline / modal |
| `QueueStatus.tsx`, `QueueStatusCell.tsx` | Status badge |
| `EpisodeCellContent.tsx`, `EpisodeTitleCellContent.tsx` | Per-episode rendering |
| `ProtocolLabel.tsx` | Usenet/Torrent badge |
| `QueueFilterModal.tsx` | Filter |
| `queueOptionsStore.ts` | Zustand options |
| `useQueue.ts` | Hook (accepts `mediaType` arg switching `/queue` ↔ `/manga/queue`) |
| `RemoveQueueItemModal.tsx` | Remove + retry / blocklist confirmation |

### History/ (~20 files)
Persistent log of every grab/import event.

| File | Purpose |
|------|---------|
| `History.tsx` | Page (accepts `mediaType?: 'series' \| 'manga'` prop — Phase 7 D-10; server-side paged) |
| `MangaHistory.tsx` | Phase 7 thin wrapper rendering `<History mediaType="manga" />` (Plan 07-09) |
| `HistoryRow.tsx` | One row |
| `HistoryEventTypeCell.tsx` | Event type badge (Grabbed/Imported/Failed/Deleted/Renamed) |
| `Details/HistoryDetailsModal.tsx` | Detail modal |
| `HistoryFilterModal.tsx` | Filter |
| `historyOptionsStore.ts` | Zustand options |
| `useHistory.ts` | Generic history hook (accepts `mediaType` arg switching `/history` ↔ `/manga/history`) |
| `useEpisodeHistory.ts` | Per-episode history |
| `useSeriesHistory.ts` | Per-series history |

### Blocklist/ (~5 files)
Releases marked as "don't retry."

| File | Purpose |
|------|---------|
| `Blocklist.tsx` | Page (accepts `mediaType?: 'series' \| 'manga'` prop — Phase 7 D-10) |
| `MangaBlocklist.tsx` | Phase 7 thin wrapper rendering `<Blocklist mediaType="manga" />` (Plan 07-09) |
| `BlocklistRow.tsx` | Row |
| `BlocklistDetailsModal.tsx` | Details |
| `BlocklistFilterModal.tsx` | Filter |
| `blocklistOptionsStore.ts` | Zustand |
| `useBlocklist.ts` | Hook (accepts `mediaType` arg switching `/blocklist` ↔ `/manga/blocklist`) |

## Server-Side Pagination

History/Queue/Blocklist are all server-paged (large data sets). They use `createServerSideCollectionHandlers` from Redux store creators rather than client-side filter/sort.

## Real-Time Updates

The SignalR `queue` and `history` messages keep these views fresh without polling. `Queue` is particularly interactive — progress bars update live.

## Manga Adaptation Notes

These views are **largely reusable**. Migration is mostly terminology:

| Mangarr term in UI | Manga term |
|-------------------|------------|
| Episode | Chapter |
| Season | Volume |
| "Episode title" column | "Chapter title" column |
| `EpisodeCellContent`, `EpisodeTitleCellContent` | Rename to `ChapterCellContent`, `ChapterTitleCellContent` |

The data shapes are minimally different (fields like `episodeId` become `chapterId`).

## Phase 7 D-10 + Lock #1 — mediaType discriminator (Plan 07-09)

Per Phase 7 D-10 (existing tables/pages drive off API URL paths; manga-mode is selected via
route + query-key namespace) and RESEARCH Lock #1 (separate top-level routes, NOT query-string
or fork), Plan 07-09 added:

**Hooks (extended in place — `useQueue.ts` / `useHistory.ts` / `useBlocklist.ts`):**
Each accepts a `mediaType: 'series' | 'manga'` arg defaulting to `'series'`. The arg switches
the `path` passed to `usePagedApiQuery` between `/queue` ↔ `/manga/queue` (and equivalents
for history + blocklist). React Query's queryKey is auto-derived from the `path` arg, giving
clean cache namespacing (`['/queue']` vs `['/manga/queue']`) — Pitfall 5 cache no-collision.

**Pages (extended in place — `Queue.tsx` / `History.tsx` / `Blocklist.tsx`):**
Each accepts `mediaType?: 'series' | 'manga'` prop defaulting to `'series'`. The prop is
forwarded to the hook. Empty-state copy switches to manga-specific i18n keys
(`QueueIsEmptyManga` / `NoHistoryFoundManga` / `NoBlocklistItemsManga`) when `mediaType ==='manga'`.

**Thin wrappers (NEW — Lock #13 Option B per RESEARCH):**

| Wrapper file | Renders | Mounted at route |
|--------------|---------|------------------|
| `Queue/MangaQueue.tsx` | `<Queue mediaType="manga" />` | `/manga/activity/queue` |
| `History/MangaHistory.tsx` | `<History mediaType="manga" />` | `/manga/activity/history` |
| `Blocklist/MangaBlocklist.tsx` | `<Blocklist mediaType="manga" />` | `/manga/activity/blocklist` |

**SignalR auto-refresh (closes Phase 6 F-01):** Plan 07-02's
`frontend/src/Components/SignalRListener.tsx` handlers for `manga/queue`, `manga/history`,
`manga/blocklist` invalidate the matching React Query keys (`['/manga/queue']`,
`['/manga/history']`, `['/manga/blocklist']`) — manga Activity pages auto-refresh on
backend events identically to how TV Activity pages auto-refresh on `queue` / `history` /
`blocklist` SignalR pushes.

**Phase 8 cleanup:** When `/manga/activity/*` is promoted (or `/activity/*` is dropped), the
thin wrappers merge into the page components (`mediaType` default flips to `'manga'`) and
the wrappers are deleted. The hooks lose the discriminator (single URL).

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../../../src/NzbDrone.Core/Queue/](../../../src/NzbDrone.Core/Queue/) — Backend Queue
- [../../../src/NzbDrone.Core/History/](../../../src/NzbDrone.Core/History/) — Backend History
- [../../../src/NzbDrone.Core/Blocklisting/](../../../src/NzbDrone.Core/Blocklisting/) — Backend Blocklist
- [../../../src/Mangarr.Api.V5/Queue/](../../../src/Mangarr.Api.V5/Queue/) — REST endpoints
