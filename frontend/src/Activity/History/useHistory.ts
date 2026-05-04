// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType discriminator added — see DIVERGENCE.md.
// Default 'series' preserves all existing TV call-sites unchanged. Manga callers pass 'manga'
// (currently the MangaHistory thin wrapper at /manga/activity/history) which switches the URL to
// /manga/history and the React Query key to ['/manga/history'] so SignalR invalidations namespace
// cleanly per Plan 07-02 (Pitfall 5 — TV/manga cache MUST NOT collide). Phase 8 collapses.
import { keepPreviousData, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo, useState } from 'react';
import { Filter, FilterBuilderProp } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import usePage from 'Helpers/Hooks/usePage';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { filterBuilderValueTypes } from 'Helpers/Props';
import History from 'typings/History';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import translate from 'Utilities/String/translate';
import { useHistoryOptions } from './historyOptionsStore';

export type HistoryMediaType = 'series' | 'manga';

export const FILTERS: Filter[] = [
  {
    key: 'all',
    label: () => translate('All'),
    filters: [],
  },
  {
    key: 'grabbed',
    label: () => translate('Grabbed'),
    filters: [
      {
        key: 'eventType',
        value: '1',
        type: 'equal',
      },
    ],
  },
  {
    key: 'imported',
    label: () => translate('Imported'),
    filters: [
      {
        key: 'eventType',
        value: '3',
        type: 'equal',
      },
    ],
  },
  {
    key: 'failed',
    label: () => translate('Failed'),
    filters: [
      {
        key: 'eventType',
        value: '4',
        type: 'equal',
      },
    ],
  },
  {
    key: 'deleted',
    label: () => translate('Deleted'),
    filters: [
      {
        key: 'eventType',
        value: '5',
        type: 'equal',
      },
    ],
  },
  {
    key: 'renamed',
    label: () => translate('Renamed'),
    filters: [
      {
        key: 'eventType',
        value: '6',
        type: 'equal',
      },
    ],
  },
  {
    key: 'ignored',
    label: () => translate('Ignored'),
    filters: [
      {
        key: 'eventType',
        value: '7',
        type: 'equal',
      },
    ],
  },
];

export const FILTER_BUILDER: FilterBuilderProp<History>[] = [
  {
    name: 'eventType',
    label: () => translate('EventType'),
    type: 'equal',
    valueType: filterBuilderValueTypes.HISTORY_EVENT_TYPE,
  },
  {
    name: 'seriesIds',
    label: () => translate('Series'),
    type: 'equal',
    valueType: filterBuilderValueTypes.SERIES,
  },
  {
    name: 'quality',
    label: () => translate('Quality'),
    type: 'equal',
    valueType: filterBuilderValueTypes.QUALITY,
  },
  {
    name: 'languages',
    label: () => translate('Languages'),
    type: 'contains',
    valueType: filterBuilderValueTypes.LANGUAGE,
  },
];

type HistoryType = 'episode' | 'series';

const MARK_AS_FAILED_QUERY_KEYS: Record<HistoryType, string> = {
  episode: '/history/episode',
  series: '/history/series',
} as const;

const useHistory = (mediaType: HistoryMediaType = 'series') => {
  const { page, goToPage } = usePage('history');
  const { pageSize, selectedFilterKey, sortKey, sortDirection } =
    useHistoryOptions();
  const customFilters = useCustomFiltersList('history');

  const filters = useMemo(() => {
    return findSelectedFilters(selectedFilterKey, FILTERS, customFilters);
  }, [selectedFilterKey, customFilters]);

  const path = mediaType === 'manga' ? '/manga/history' : '/history';

  const { refetch, ...query } = usePagedApiQuery<History>({
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

  const handleGoToPage = useCallback(
    (page: number) => {
      goToPage(page);
    },
    [goToPage]
  );

  return {
    ...query,
    goToPage: handleGoToPage,
    page,
    refetch,
  };
};

export default useHistory;

export const useFilters = () => {
  return FILTERS;
};

export const useMarkAsFailed = (id: number, type?: HistoryType) => {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);

  const { mutate, isPending } = useApiMutation<unknown, void>({
    path: `/history/failed/${id}`,
    method: 'POST',
    mutationOptions: {
      onMutate: () => {
        setError(null);
      },
      onSuccess: () => {
        const queryKey = type ? MARK_AS_FAILED_QUERY_KEYS[type] : '/history';

        queryClient.invalidateQueries({ queryKey: [queryKey] });
      },
      onError: () => {
        setError('Error marking history item as failed');
      },
    },
  });

  return {
    markAsFailed: mutate,
    isMarkingAsFailed: isPending,
    markAsFailedError: error,
  };
};
