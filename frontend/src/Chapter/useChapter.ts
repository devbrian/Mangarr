// Sonarr divergence: NEW manga sibling per Phase 7 D-07 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/useEpisode.ts (cache-keyed Episode
// lookup with EpisodeEntity discriminator).
//
// Manga sibling preserves: React Query cache shape (URL-shaped key), single +
// list reads, monitor-toggle mutation hook contract.
//
// Manga sibling diverges from useEpisode:
//   * Reads from URL-shaped React Query keys (`['/chapter']`,
//     `['/chapter', { mangaId }]`) — Plan 07-02 SignalR contract — instead of
//     the EpisodeEntity → store-of-stored-keys indirection. Manga inherits
//     Phase 7's URL-key contract from useManga / Plan 07-02; Sonarr's older
//     store-of-store pattern is not carried over.
//   * Bulk monitor PUT writes to /chapter/monitor (Plan 07-01 endpoint);
//     single-row monitor PUT writes to /chapter/{id}.
//   * No EpisodeEntity discriminator (calendar / wanted / etc.) — those
//     consumers will arrive in later plans and use `useApiQuery` directly.
//
// Phase 8 cleanup: collapse with useEpisode when Tv/ deletes.
import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Chapter from './Chapter';

const DEFAULT_CHAPTERS: Chapter[] = [];

/**
 * Fetch all chapters for a manga via GET /api/v5/chapter?mangaId={id}.
 * Backed by Plan 07-01 ChapterController. Cache key: `['/chapter', { mangaId }]`.
 */
export const useChaptersByManga = (mangaId: number | undefined) => {
  const { data, ...result } = useApiQuery<Chapter[]>({
    path: '/chapter',
    queryParams: mangaId !== undefined ? { mangaId } : undefined,
    queryOptions: {
      enabled: mangaId !== undefined,
      staleTime: 5 * 60 * 1000,
    },
  });

  return {
    ...result,
    data: data ?? DEFAULT_CHAPTERS,
  };
};

/**
 * Fetch a single chapter via GET /api/v5/chapter/{id}.
 * Cache key: `['/chapter/{id}']`.
 */
export const useSingleChapter = (chapterId: number | undefined) => {
  return useApiQuery<Chapter>({
    path: chapterId !== undefined ? `/chapter/${chapterId}` : '/chapter',
    queryOptions: {
      enabled: chapterId !== undefined,
    },
  });
};

interface ToggleChapterMonitoredPayload {
  monitored: boolean;
}

interface ToggleChaptersMonitoredPayload {
  chapterIds: number[];
  monitored: boolean;
}

/**
 * Toggle monitor on a single chapter — PUT /api/v5/chapter/{id} with the
 * full chapter resource body (Plan 07-01 RestPutById). The Manga/Details
 * Chapters tab uses this for per-row toggles.
 */
export const useToggleChapterMonitored = (chapter: Chapter | undefined) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    Chapter,
    Chapter & ToggleChapterMonitoredPayload
  >({
    path: chapter ? `/chapter/${chapter.id}` : '/chapter',
    method: 'PUT',
    mutationOptions: {
      onSuccess: (updated) => {
        // WR-03 fix: target ONLY list caches by restricting the query filter
        // to keys whose first element is EXACTLY '/chapter' (the list path).
        // The previous filter `{ queryKey: ['/chapter'] }` is correct in
        // current TanStack Query semantics (element-wise prefix match — does
        // not hit `['/chapter/123']` single-chapter caches), but the setter
        // also defensively guards against any future query whose value is
        // not an array, so `.map` is never called on a non-Chapter[] cache.
        // Single-chapter caches are kept consistent via the SignalR
        // `chapter` invalidation handler (Plan 07-02).
        queryClient.setQueriesData<Chapter[]>(
          {
            queryKey: ['/chapter'],
            exact: false,
            predicate: (q) =>
              Array.isArray(q.queryKey) && q.queryKey[0] === '/chapter',
          },
          (prev) => {
            if (!Array.isArray(prev)) {
              return prev;
            }

            return prev.map((c) => (c.id === updated.id ? { ...c, ...updated } : c));
          }
        );
      },
    },
  });

  return {
    toggleChapterMonitored: (monitored: boolean) => {
      if (!chapter) {
        return;
      }
      mutate({ ...chapter, monitored });
    },
    isToggling: isPending,
    toggleError: error,
  };
};

/**
 * Bulk monitor toggle — PUT /api/v5/chapter/monitor (Plan 07-01 endpoint).
 * Defers cache invalidation to the SignalR `chapter` handler (Plan 07-02).
 */
export const useBulkToggleChaptersMonitored = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    Chapter[],
    ToggleChaptersMonitoredPayload
  >({
    path: '/chapter/monitor',
    method: 'PUT',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/chapter'] });
      },
    },
  });

  return {
    bulkToggleChaptersMonitored: mutate,
    isBulkToggling: isPending,
    bulkToggleError: error,
  };
};

export default useChaptersByManga;
