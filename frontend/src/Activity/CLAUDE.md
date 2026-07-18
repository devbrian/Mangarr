# Activity/

## Purpose

The "Activity" section of the app — three views into download lifecycle:

- **Queue** — currently in-flight downloads
- **History** — completed grabs and imports (success and failure)
- **Blocklist** — releases that failed and shouldn't be retried

## Subdirectories

### Queue/ (~20 files)
Live view of active downloads from the configured download clients.

| File | Purpose |
|------|---------|
| `Queue.tsx` | Page (accepts `mediaType?: 'series' \| 'manga'` prop — Phase 7 D-10) |
| `MangaQueue.tsx` | Phase 7 thin wrapper rendering `<Queue mediaType="manga" />` (Plan 07-09) |
| `QueueRow.tsx` | One queue item |
| `QueueDetails.tsx`, `Details/QueueDetailsProvider.tsx` | Details inline / modal. `QueueDetailsProvider` repointed (2026-05-09) from the deleted TV `/queue/details` route onto `/manga/queue/details` (Phase 13 Plan 13-08 `MangaQueueDetailsController`); filter params mapped from TV shape (`seriesId` / `episodeIds`) to manga shape (`mangaId` / `chapterIds`); the legacy `all=true` discriminator dropped (manga endpoint returns the full queue with no filter). Helper hooks (`useQueueDetailsForSeries` / `useQueueItemForEpisode` / `useIsDownloadingEpisodes`) fall back to manga-shape fields when TV-shape ones are absent. React Query key now matches the `SignalRListener.tsx:357-364` `manga/queue/details` invalidation handler. |
| `QueueStatus.tsx`, `QueueStatusCell.tsx`, `Status/useQueueStatus.ts` | Status badge (sidebar count + queue page header). `useQueueStatus` repointed (2026-05-10, issue #45) from the deleted TV `/queue/status` route onto `/manga/queue/status` (Phase 13 Plan 13-09 `MangaQueueStatusController`). The `QueueStatus` TypeScript interface is unchanged because `MangaQueueStatusResource` mirrors `QueueStatusResource` field-for-field (`TotalCount` / `Count` / `UnknownCount` / `Errors` / `Warnings` / `UnknownErrors` / `UnknownWarnings`). React Query key now matches the `SignalRListener.tsx:366-380` `manga/queue/status` `setQueryData` handler — pre-fix the cache key was `['/queue/status']` so SignalR pushes were silently dropped on the floor (latent cache-staleness bug fixed alongside the 404). |
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

**Note (Phase 17.3 D-07):** Phase 15 Plan 15-12 shipped `EpisodeCellContent.tsx`
and `EpisodeTitleCellContent.tsx` as 4-line `return null` no-op stubs to
satisfy the verbatim Sonarr cell-content slot. Phase 17.3 Plan 17.3-04
deleted both — no chapter-domain consumer ever existed (only this CLAUDE.md
referenced them). If a chapter-domain cell-content is needed later, author
it fresh against a real consumer rather than reviving a stub.

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


## GH issue #73 (2026-05-11) — QueueRow / HistoryRow / BlocklistRow manga-shape migration (Plan 15-12 follow-up)

The Activity Queue / History / Blocklist row components were rewritten to consume manga wire shapes directly, replacing the TV-shape `seriesId` / `episodeIds` / `quality` / `customFormats` props with manga-shape `mangaId` / `chapterId` / `translatedLanguage` / `scanlationGroup`. The previous PR #74 added crash-prevention defaults (`episodeIds = []` / `seasonNumbers = []`) so the queue could render at all post-Phase-15 cutover; this PR completes the migration so the cells actually show values.

**Pattern adopted (mirrors `Wanted/Missing/MissingRow.tsx` precedent):**

- **Row components** consume manga wire shape directly via spread (`{...item}`) from page-level `MangaQueueItem[]` / `ChapterHistory[]` / `MangaBlocklist[]` records. Resolve `manga` / `chapter` via hydrated subresource preferred over `useSingleManga(mangaId)` / `useSingleChapter(chapterId)` fallback from the React Query caches (`['/manga']` / `['/chapter']`). No additional network fetches in the common path.
- **Column keys preserved verbatim** from the pre-fix TV-shape registries (`series.sortTitle`, `episode`, `episodes.title`) so persisted Zustand state continues to work without a localStorage migration; labels flip to manga terminology in the option stores (Manga / Chapter / Chapter Title).
- **New columns** `translatedLanguage` (renders `Chapter/LanguageBadge`) + `scanlationGroup` (plain text) appended to Queue + History; History keeps `releaseGroup` as a backward-compat alias for `scanlationGroup`.
- **TV-only columns dropped from defaults:** `quality`, `customFormats`, `customFormatScore`, `languages`, `episodes.airDateUtc` (manga has no quality model per Phase 5 D-04).
- **Store names bumped** (`manga_queue_options`, `manga_history_options`, `manga_blocklist_options`) so users on the upgrade path hydrate a clean registry; pre-existing `queue_options` / `history_options` / `blocklist_options` localStorage entries become orphaned (no data loss — column-visibility + page-size + sort-key only).
- **Hook types retyped:** `useQueue` → `MangaQueueItem`; `useHistory` → `ChapterHistory`; `useBlocklist` → `MangaBlocklist`. The `@ts-expect-error` directives on `Queue.tsx:272` and `History.tsx:207` are removed; the dead-code `useEpisodes` / `useEpisodesWithIds` lookups (Plan 15-12 era stubs returning `[]`) are removed.

**Phase 8 cleanup target:** when Tv/ deletes, the column keys can rename to `manga.X` / `chapter.X` (one-off Zustand migration at that point). The MangaQueue / MangaHistory / MangaBlocklist thin wrappers collapse into the page components.

## Source column (quick-260615-edl) — per-release source-key attribution

All three Activity tables (Queue / History / Blocklist) carry a toggleable **"Source"** column, **visible by default**, rendering the per-release source (the gateway's underlying manga source — e.g. `mangadot`, `mangafire`, `mangadex`). Since Phase 39 retired the in-process scrapers and `GatewayIndexer` became the sole indexer, the existing (hidden) "Indexer" column is always the literal string "Gateway" and can no longer distinguish releases; the per-release source is now the meaningful attribution.

**⚠ The source value comes from a DIFFERENT field per surface (live-verified 2026-06-15) — they are NOT uniform.** The top-level `sourceKey` field on History/Blocklist holds the CONSTANT indexer name (`"Mangarr Gateway"`), not the per-release source — rendering it would show the same useless string on every row. Only the **Queue** surface's `sourceKey` (= `ReleaseInfo.Source` = `GatewayRelease.SourceKey`) carries the real per-source token. For History/Blocklist the real source lives in the **leading colon-segment of the release guid** (`source:mangaId:ch:lang:relId`), which is verified to equal the gateway's `sourceKey` token (`mangafire:…` ⇒ `mangafire`).

- **Column key** `source` (visible-by-default, `isSortable: false`); registered in `queueOptionsStore.ts` (after `scanlationGroup`, before `protocol`), `historyOptionsStore.ts` (after `scanlationGroup`, before `date`), and `blocklistOptionsStore.ts` (after `translatedLanguage`, before `date`). Reuses the existing `translate('Source')` i18n key — no new key. No store-name bump (a visible column addition is spliced into place by `useOptionsStore.mergeColumns()` for existing persisted users).
- **Cell render** in each Row's `columns.map` switch on `name === 'source'`, with the `data-testid` convention (`manga-{queue,history,blocklist}-row-${id}-source`):
  - **Queue** — renders the top-level `sourceKey` prop directly (empty-string fallback). This is the only surface whose `sourceKey` is the real per-release source.
  - **History / Blocklist** — render `parseSourceFromGuid(releaseGuid)` (`Activity/parseSourceFromGuid.ts` — splits on the first `:`). History imported-history events carry no release guid, so the History Source column falls back to the persisted `data.source` (the real gateway source resolved at download time). Blank only for legacy rows persisted before either field existed and for legacy blocklist rows persisted before the guid was populated (`260613-gtv`).
- **Wire shape:** History (`ChapterHistory.releaseGuid`) and Blocklist (`MangaBlocklist.releaseGuid`) already carried the guid end-to-end. The manga Queue was the backend gap — `MangaQueueResource.SourceKey` (sourced from `ReleaseInfo.Source` in `MangaQueueService.MapQueueItem`) + `typings/MangaQueueItem.ts` `sourceKey?` closed it (see `src/Mangarr.Api.V5/Manga/Queue/CLAUDE.md`).

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../../../src/NzbDrone.Core/Queue/](../../../src/NzbDrone.Core/Queue/) — Backend Queue
- [../../../src/NzbDrone.Core/History/](../../../src/NzbDrone.Core/History/) — Backend History
- [../../../src/NzbDrone.Core/Blocklisting/](../../../src/NzbDrone.Core/Blocklisting/) — Backend Blocklist
- [../../../src/Mangarr.Api.V5/Manga/Queue/](../../../src/Mangarr.Api.V5/Manga/Queue/) — REST endpoints
