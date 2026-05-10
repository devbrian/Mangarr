// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/SeriesDetailsProvider.tsx
// (Series wraps QueueDetailsProvider + EpisodeFileContext.Provider; manga
// sibling defers ChapterFile context to Plans 07-08+ — Files tab).
//
// Manga sibling preserves: provider-pass-through shape.
// Manga sibling diverges from SeriesDetailsProvider:
//   * No ChapterFile context — Plan 07-05 ships the Chapters tab; the
//     ChapterFile subresource lands with the Files tab in a future plan.
//   * No QueueDetails wrap — manga queue details (per-chapter download
//     progress) is wired via the URL-shaped React Query cache key
//     `['/manga/queue']` directly inside ChapterStatus.
//   * DOES wrap a per-manga ChapterHistory context (issue #51 fix). The
//     prior per-row useApiQuery({ path: '/manga/history', queryParams: {
//     chapterId } }) call inside ChapterStatus fanned out to N HTTP requests
//     when the Chapters tab opened (one per chapter row). This provider
//     fetches the whole-manga history ONCE via the existing `mangaIds[]`
//     filter on the same backend endpoint, buckets the descending-by-date
//     records by chapterId, and exposes them via context. ChapterStatus
//     reads the bucketed result instead of firing its own query.
//
// Phase 8 cleanup: collapse with SeriesDetailsProvider when Tv/ deletes.
import React, { PropsWithChildren, useMemo } from 'react';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import ChapterHistory from 'typings/ChapterHistory';
import {
  MangaChapterHistoryContext,
  MangaChapterHistoryContextValue,
} from './MangaChapterHistoryContext';

interface MangaDetailsProviderProps {
  mangaId: number;
}

// Backend page-size cap for the lifted history fetch. Mirrors the
// MangaDetailsHistory tab's PAGE_SIZE. Long-running manga (One Piece at
// 1100+ chapters) may have history records that exceed this cap; the
// "isFailed" status on chapters whose most-recent failed-event falls
// off the first page would silently degrade to "not failed". This is an
// accepted v1 trade-off — the prior per-row fan-out also degraded silently
// when the user navigated away mid-fetch. A future enhancement could
// page-walk-until-empty when totalRecords > PAGE_SIZE if the false-negative
// rate proves problematic in practice.
const HISTORY_PAGE_SIZE = 250;

function MangaDetailsProvider({
  mangaId,
  children,
}: PropsWithChildren<MangaDetailsProviderProps>) {
  // Lifted from ChapterStatus.tsx (issue #51 fix). Same backend endpoint,
  // same descending-by-date sort discipline (WR-05 — guarantees records[0]
  // is the most-recent event for any given chapter). The mangaIds[] filter
  // already exists on ChapterHistoryController.GetHistory (Phase 6 Plan
  // 06-09); MangaDetailsHistory.tsx already uses the same shape for the
  // History tab — this just adds a second consumer of the same fetch.
  //
  // Filter shape mirrors MangaDetailsHistory.tsx:67-99 — see comments there
  // for the PropertyFilter -> ?mangaIds=<id> wire-format translation.
  const filters = useMemo(
    () => [
      {
        key: 'mangaIds',
        value: [mangaId],
        type: 'equal' as const,
      },
    ],
    [mangaId]
  );

  const { records, isFetched } = usePagedApiQuery<ChapterHistory>({
    path: '/manga/history',
    page: 1,
    pageSize: HISTORY_PAGE_SIZE,
    sortKey: 'date',
    sortDirection: 'descending',
    filters,
    queryOptions: {
      // Match the prior per-row staleTime so SignalR-pushed history
      // invalidations behave the same as before (history is volatile;
      // a 30s stale window matches the queue/blocklist staleness in
      // ChapterStatus.tsx).
      staleTime: 30 * 1000,
    },
  });

  // Bucket the records by chapterId. records is already sorted descending
  // by date (server-side), so the per-chapter array is in most-recent-first
  // order without an additional client-side sort. Empty map when records
  // is the DEFAULT_RECORDS empty-array sentinel.
  const value = useMemo<MangaChapterHistoryContextValue>(() => {
    const map = new Map<number, ChapterHistory[]>();
    for (const record of records) {
      const existing = map.get(record.chapterId);
      if (existing) {
        existing.push(record);
      } else {
        map.set(record.chapterId, [record]);
      }
    }
    return {
      chapterHistoryByChapterId: map,
      isFetched,
    };
  }, [records, isFetched]);

  return (
    <MangaChapterHistoryContext.Provider value={value}>
      {children}
    </MangaChapterHistoryContext.Provider>
  );
}

export default MangaDetailsProvider;
