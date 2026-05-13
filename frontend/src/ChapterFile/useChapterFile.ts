// Sonarr divergence: NEW manga sibling per issue #84 (Plan 17.3-16 deferral
// resolution — Option A). See DIVERGENCE.md.
//
// Role-match analog: frontend/src/EpisodeFile/useEpisodeFiles.ts on the
// `v5-develop` Sonarr reference branch (cache-keyed EpisodeFile list/single
// reads + delete mutations).
//
// Manga sibling preserves: URL-shaped React Query cache key + Plan 07-02
// SignalR invalidation contract — list reads land at `['/chapterFile']`,
// SignalR `chapterfile` handler invalidates the same key on backend
// ChapterFileAddedEvent / ChapterFileDeletedEvent fan-out
// (SignalRListener.tsx Plan 13-07 chapterfile handler).
//
// Manga sibling diverges from useEpisodeFiles:
//   * Path is `/chapterFile` (manga peer of `/episodeFile`; backend
//     ASP.NET Core case-insensitive routing matches `/ChapterFile` too).
//   * Query param is `mangaId` (peer of `seriesId`) or `chapterFileIds`.
//   * No EpisodeEntity-keyed invalidation (manga has no peer
//     entity-discriminator; cache key is the bare URL).
//   * No `useUpdateEpisodeFiles` — the backend ChapterFileController carries
//     NO PUT endpoint (Plan 13-07 design — manga has no Quality field, and
//     per-file ScanlationGroup edits arrive via the editor flow rather than
//     the file controller). Add `useUpdateChapterFiles` here only when a
//     matching backend PUT lands.
//
// Backend endpoints consumed (Plan 13-07 ChapterFileController.cs):
//   * GET  /api/v5/ChapterFile?mangaId={id}          — list by parent
//   * GET  /api/v5/ChapterFile?chapterFileIds=...    — list by ids
//   * DELETE /api/v5/ChapterFile/{id}                — single-row delete
//   * DELETE /api/v5/ChapterFile/bulk                — bulk delete (body: { chapterFileIds })
//
// Phase 8 cleanup: was — collapse with useEpisodeFiles when Tv/ deletes;
// Phase 17.3 Plan 17.3-13 already retired the TV stub-dir family.
import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import ChapterFile from './ChapterFile';

const DEFAULT_CHAPTER_FILES: ChapterFile[] = [];

/**
 * Fetch all chapter files for a manga via GET /api/v5/ChapterFile?mangaId={id}.
 * Backed by Plan 13-07 ChapterFileController. Cache key: `['/chapterFile', { mangaId }]`.
 */
export const useChapterFilesByManga = (mangaId: number | undefined) => {
  const { data, ...result } = useApiQuery<ChapterFile[]>({
    path: '/chapterFile',
    queryParams: mangaId !== undefined ? { mangaId } : undefined,
    queryOptions: {
      enabled: mangaId !== undefined,
      staleTime: 30 * 1000,
    },
  });

  return {
    ...result,
    data: data ?? DEFAULT_CHAPTER_FILES,
    hasChapterFiles: !!data?.length,
  };
};

interface ChapterFileIds {
  chapterFileIds: number[];
}

/**
 * Delete a single chapter file via DELETE /api/v5/ChapterFile/{id}.
 * Invalidates the list cache + the chapter cache (a chapter's `hasFile` /
 * `chapterFileId` flips on delete; SignalR also handles this via the
 * `chapterfile` handler but the immediate invalidation gives instant UI
 * feedback for the user-initiated action).
 */
export const useDeleteChapterFile = (id: number) => {
  const queryClient = useQueryClient();

  const { mutate, error, isPending } = useApiMutation<unknown, void>({
    path: `/chapterFile/${id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/chapterFile'] });
        queryClient.invalidateQueries({ queryKey: ['/chapter'] });
      },
    },
  });

  return {
    deleteChapterFile: mutate,
    isDeleting: isPending,
    deleteError: error,
  };
};

/**
 * Bulk delete chapter files via DELETE /api/v5/ChapterFile/bulk with
 * body `{ chapterFileIds: number[] }`.
 */
export const useDeleteChapterFiles = () => {
  const queryClient = useQueryClient();

  const { mutate, error, isPending } = useApiMutation<unknown, ChapterFileIds>({
    path: '/chapterFile/bulk',
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/chapterFile'] });
        queryClient.invalidateQueries({ queryKey: ['/chapter'] });
      },
    },
  });

  return {
    deleteChapterFiles: mutate,
    isDeleting: isPending,
    deleteError: error,
  };
};

export default useChapterFilesByManga;
