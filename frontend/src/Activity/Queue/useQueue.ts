// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType discriminator added — see DIVERGENCE.md.
// Phase 15 Plan 15-07 Wave 3 (Sub-step F option (a)): default flipped 'series' -> 'manga' since
// TV is gone post-cutover; the type union 'series' | 'manga' collapsed to 'manga' (preserves the
// discriminator type for v2 reintroduction). The /queue URL branch is dead code now but the
// conditional is left in place so v2 can flip the union back without re-deriving the URL switch.
//
// GH issue #73 (2026-05-11) — type parameter retyped from legacy `Queue` (TV)
// to `MangaQueueItem` so the records emitted by `usePagedApiQuery` carry the
// manga wire shape (mangaId / chapterId / chapterIds / translatedLanguage /
// scanlationGroup) consumed by the rewritten QueueRow. Mirrors useHistory's
// MangaHistoryItem typing precedent.
import { keepPreviousData, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Filter, FilterBuilderProp } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import usePage from 'Helpers/Hooks/usePage';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { filterBuilderValueTypes } from 'Helpers/Props';
import MangaQueueItem from 'typings/MangaQueueItem';
import getQueryString from 'Utilities/Fetch/getQueryString';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import translate from 'Utilities/String/translate';
import { useQueueOptions } from './queueOptionsStore';

export type QueueMediaType = 'manga';

interface BulkQueueData {
  ids: number[];
}

export const FILTERS: Filter[] = [
  {
    key: 'all',
    label: () => translate('All'),
    filters: [],
  },
  {
    key: 'excludeUnknownSeriesItems',
    label: () => translate('ExcludeUnknownSeriesItems'),
    filters: [
      {
        key: 'includeUnknownSeriesItems',
        value: [false],
        type: 'equal',
      },
    ],
  },
];

export const FILTER_BUILDER: FilterBuilderProp<MangaQueueItem>[] = [
  {
    name: 'mangaIds',
    label: () => translate('MangaTitle'),
    type: 'equal',
    valueType: filterBuilderValueTypes.SERIES,
  },
  {
    name: 'protocol',
    label: () => translate('Protocol'),
    type: 'equal',
    valueType: filterBuilderValueTypes.PROTOCOL,
  },
  {
    name: 'status',
    label: () => translate('Status'),
    type: 'equal',
    valueType: filterBuilderValueTypes.QUEUE_STATUS,
  },
];

const useQueue = (mediaType: QueueMediaType = 'manga') => {
  const { page, goToPage } = usePage('queue');
  const { pageSize, selectedFilterKey, sortKey, sortDirection } =
    useQueueOptions();
  const customFilters = useCustomFiltersList('queue');

  const filters = useMemo(() => {
    return findSelectedFilters(selectedFilterKey, FILTERS, customFilters);
  }, [selectedFilterKey, customFilters]);

  const path = mediaType === 'manga' ? '/manga/queue' : '/queue';

  const { refetch, ...query } = usePagedApiQuery<MangaQueueItem>({
    path,
    page,
    pageSize,
    filters,
    sortKey,
    sortDirection,
    queryOptions: {
      placeholderData: keepPreviousData,
    },
  });

  return {
    ...query,
    goToPage,
    page,
    refetch,
  };
};

export default useQueue;

export const useFilters = () => {
  return FILTERS;
};

const useRemovalOptions = () => {
  const { removalOptions } = useQueueOptions();

  return {
    remove: removalOptions.removalMethod === 'removeFromClient',
    changeCategory: removalOptions.removalMethod === 'changeCategory',
    blocklist: removalOptions.blocklistMethod !== 'doNotBlocklist',
    skipRedownload: removalOptions.blocklistMethod === 'blocklistOnly',
  };
};

export const useRemoveQueueItem = (id: number) => {
  const queryClient = useQueryClient();
  const removalOptions = useRemovalOptions();

  const { mutate, isPending } = useApiMutation<unknown, void>({
    path: `/queue/${id}${getQueryString(removalOptions)}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/queue'] });
      },
    },
  });

  return {
    removeQueueItem: mutate,
    isRemoving: isPending,
  };
};

export const useRemoveQueueItems = () => {
  const queryClient = useQueryClient();
  const removalOptions = useRemovalOptions();

  const { mutate, isPending } = useApiMutation<unknown, BulkQueueData>({
    path: `/queue/bulk${getQueryString(removalOptions)}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/queue'] });
      },
    },
  });

  return {
    removeQueueItems: mutate,
    isRemoving: isPending,
  };
};

export const useGrabQueueItem = (id: number) => {
  const queryClient = useQueryClient();
  const [grabError, setGrabError] = useState<string | null>(null);

  const { mutate, isPending } = useApiMutation<unknown, void>({
    path: `/queue/grab/${id}`,
    method: 'POST',
    mutationOptions: {
      onMutate: () => {
        setGrabError(null);
      },
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/queue'] });
      },
      onError: () => {
        setGrabError('Error grabbing queue item');
      },
    },
  });

  return {
    grabQueueItem: mutate,
    isGrabbing: isPending,
    grabError,
  };
};

export const useGrabQueueItems = () => {
  const queryClient = useQueryClient();

  // Explicitly define the types for the mutation so we can pass in no arguments to mutate as expected.
  const { mutate, isPending } = useApiMutation<unknown, BulkQueueData>({
    path: '/queue/grab/bulk',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/queue'] });
      },
    },
  });

  return {
    grabQueueItems: mutate,
    isGrabbing: isPending,
  };
};
