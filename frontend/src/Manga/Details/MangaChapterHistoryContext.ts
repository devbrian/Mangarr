// Sonarr divergence: NEW manga sibling — context wrapper for the lifted
// per-manga chapter-history fetch. Resolves issue #51 (per-chapter
// /api/v5/manga/history?chapterId=X N+1 fan-out — 68 serial requests
// when opening a 68-chapter manga's Chapters tab).
//
// Role-match analog: frontend/src/Activity/Queue/Details/QueueDetailsProvider.tsx
// (Sonarr canonical pattern — parent-fetched whole-resource history bucketed
// by per-row id, consumed via useQueueItemForEpisode-style context hook).
//
// Pattern:
//   1. MangaDetailsProvider fires ONE usePagedApiQuery against
//      /api/v5/manga/history?mangaIds=<id>&sortKey=date&sortDirection=descending
//   2. Provider buckets the descending-by-date records into
//      Map<chapterId, ChapterHistory[]> via useMemo.
//   3. Provider stuffs the Map + isFetched flag onto this Context.
//   4. ChapterStatus consumes useLastChapterHistoryEvent(chapter.id) and
//      reads chapterHistoryByChapterId.get(chapterId)?.[0]?.eventType — the
//      first entry is the most-recent event because the parent fetch sorted
//      descending by date (matches the WR-05 sort discipline that the prior
//      per-row useApiQuery applied).
//
// Phase 8 cleanup: collapse with the SeriesDetailsProvider-side equivalent
// when Tv/ deletes (Sonarr proper has no direct analog because Episode carries
// a backend `grabbed` flag — manga has no equivalent, so this context stays).
import { createContext, useContext } from 'react';
import ChapterHistory from 'typings/ChapterHistory';

export interface MangaChapterHistoryContextValue {
  // Most-recent-event-first per chapterId. Empty Map when isFetched=false or
  // when the manga has no history records at all.
  chapterHistoryByChapterId: Map<number, ChapterHistory[]>;
  isFetched: boolean;
}

const EMPTY_VALUE: MangaChapterHistoryContextValue = {
  chapterHistoryByChapterId: new Map<number, ChapterHistory[]>(),
  isFetched: false,
};

export const MangaChapterHistoryContext =
  createContext<MangaChapterHistoryContextValue>(EMPTY_VALUE);

/**
 * Hook for ChapterStatus (and any future per-chapter consumer that needs
 * the most-recent history event for a chapter). Returns the most-recent
 * ChapterHistory record OR undefined if the manga has no history for that
 * chapter (or if the provider hasn't finished its first fetch yet).
 *
 * Replaces the per-row useApiQuery({ path: '/manga/history', queryParams: {
 * chapterId } }) call in ChapterStatus.tsx that caused issue #51's N+1 fan-out.
 */
export function useLastChapterHistoryEvent(
  chapterId: number
): ChapterHistory | undefined {
  const { chapterHistoryByChapterId } = useContext(MangaChapterHistoryContext);
  return chapterHistoryByChapterId.get(chapterId)?.[0];
}
