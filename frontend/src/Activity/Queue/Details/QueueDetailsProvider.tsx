// Sonarr divergence: Phase 13 Plan 13-08 backfilled `MangaQueueDetailsController` at
// `/api/v5/manga/queue/details` as the canonical replacement for the deleted TV
// `QueueDetailsController` (`/api/v5/queue/details`). The frontend was never repointed
// during Phase 13 (per the controller's own divergence comment + D-13-16 additive-only
// mandate); this file repoints all three call sites — MangaIndex (`all=true` shape),
// Wanted/Missing (`episodeIds` filter), Wanted/CutoffUnmet (`episodeIds` filter) — onto
// the manga route. Filter params are mapped from TV shape (seriesId / episodeIds) to
// manga shape (mangaId / chapterIds); the `all` discriminator is dropped because the
// manga endpoint returns the full queue when no filter is supplied
// (`MangaQueueDetailsController.cs:144-147`).
//
// Helpers (`useQueueItemForEpisode` / `useIsDownloadingEpisodes` / `useQueueDetailsForSeries`)
// fall back to manga-shape fields (`chapterIds` / `mangaId`) when the TV-shape ones
// (`episodeIds` / `seriesId`) are absent, so the same provider works for both shapes
// during the migration. The `MangaQueueResource` does not carry `episodesWithFilesCount`
// / `seasonNumbers`; those reduce branches degrade to 0 / always-true respectively, which
// matches today's runtime behaviour where the 404 left the data undefined.
//
// SignalR query-key alignment: the manga route's auto-derived React Query key is
// `['/manga/queue/details']`, which matches the `Components/SignalRListener.tsx:357-364`
// `manga/queue/details` invalidation handler. Previously the `['/queue/details']` key
// never matched the SignalR push, so even if the route had worked the cache would not
// invalidate on backend events.
//
// Phase 8 cleanup: when `Tv/` deletes and the unified `Queue` shape lands, the helper
// dual-field reads collapse to the unified field names.
import React, {
  createContext,
  PropsWithChildren,
  useContext,
  useMemo,
} from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Queue from 'typings/Queue';
import { QueryParams } from 'Utilities/Fetch/getQueryString';

interface EpisodeDetails {
  episodeIds: number[];
}

interface SeriesDetails {
  seriesId: number;
}

interface AllDetails {
  all: boolean;
}

type QueueDetailsFilter = AllDetails | EpisodeDetails | SeriesDetails;

// Manga-shape fields layered onto the TV `Queue` typing for graceful field-fallback in
// the helpers below. The runtime payload is `MangaQueueResource` (Phase 6 Plan 06-09);
// the TV-shape `episodeIds` / `seriesId` / `episodesWithFilesCount` / `seasonNumbers`
// fields are absent. Phase 8 cleanup: collapse to a unified queue typing when Tv/ deletes.
type QueueWithMangaShape = Queue & {
  mangaId?: number;
  chapterIds?: number[];
};

const QueueDetailsContext = createContext<QueueWithMangaShape[] | undefined>(
  undefined
);

function mapFilterToMangaQueryParams(filter: QueueDetailsFilter): QueryParams {
  // The manga endpoint accepts `mangaId` (singular int) or `chapterIds` (int[]). The
  // legacy `all` discriminator is dropped — `MangaQueueDetailsController` returns the
  // full queue when neither filter is set.
  if ('seriesId' in filter) {
    return { mangaId: filter.seriesId };
  }

  if ('episodeIds' in filter) {
    return { chapterIds: filter.episodeIds };
  }

  return {};
}

export default function QueueDetailsProvider({
  children,
  ...filter
}: PropsWithChildren<QueueDetailsFilter>) {
  const queryParams = useMemo(
    () => mapFilterToMangaQueryParams(filter as QueueDetailsFilter),
    [filter]
  );

  const { data } = useApiQuery<QueueWithMangaShape[]>({
    path: '/manga/queue/details',
    queryParams,
    queryOptions: {
      enabled: Object.keys(filter).length > 0,
    },
  });

  return (
    <QueueDetailsContext.Provider value={data}>
      {children}
    </QueueDetailsContext.Provider>
  );
}

export function useQueueItemForEpisode(episodeId: number) {
  const queue = useContext(QueueDetailsContext);

  return useMemo(() => {
    return queue?.find(
      (item) =>
        item.episodeIds?.includes(episodeId) ||
        item.chapterIds?.includes(episodeId)
    );
  }, [episodeId, queue]);
}

export function useIsDownloadingEpisodes(episodeIds: number[]) {
  const queue = useContext(QueueDetailsContext);

  return useMemo(() => {
    if (!queue) {
      return false;
    }

    return queue.some(
      (item) =>
        item.episodeIds?.some((e) => episodeIds.includes(e)) ||
        item.chapterIds?.some((c) => episodeIds.includes(c))
    );
  }, [episodeIds, queue]);
}

export interface SeriesQueueDetails {
  count: number;
  episodesWithFiles: number;
}

export function useQueueDetailsForSeries(
  seriesId: number,
  seasonNumber?: number
) {
  const queue = useContext(QueueDetailsContext);

  return useMemo<SeriesQueueDetails>(() => {
    if (!queue) {
      return { count: 0, episodesWithFiles: 0 };
    }

    return queue.reduce<SeriesQueueDetails>(
      (acc: SeriesQueueDetails, item) => {
        // Manga-shape rows carry `mangaId` (id alias the caller still supplies via the
        // `seriesId` arg name for MangaIndexProgressBar back-compat); TV-shape rows
        // carry `seriesId`. Match either.
        const itemOwnerId = item.seriesId ?? item.mangaId;

        if (
          item.trackedDownloadState === 'imported' ||
          itemOwnerId !== seriesId
        ) {
          return acc;
        }

        // Manga has no seasons (Phase 6 D-03 monitor enum is chapter-shaped); fall through
        // when seasonNumber is provided against a manga row whose `seasonNumbers` is
        // absent. This preserves TV-shape filtering semantics without erroring on manga.
        if (
          seasonNumber != null &&
          item.seasonNumbers != null &&
          !item.seasonNumbers.includes(seasonNumber)
        ) {
          return acc;
        }

        // `episodeIds` (TV) or `chapterIds` (manga); fall back to 0 if neither exists.
        acc.count += item.episodeIds?.length ?? item.chapterIds?.length ?? 0;
        // `episodesWithFilesCount` is a TV-only field; manga rows degrade to 0.
        acc.episodesWithFiles += item.episodesWithFilesCount ?? 0;

        return acc;
      },
      {
        count: 0,
        episodesWithFiles: 0,
      }
    );
  }, [seriesId, seasonNumber, queue]);
}

export const useQueueDetails = () => {
  return useContext(QueueDetailsContext) ?? [];
};
