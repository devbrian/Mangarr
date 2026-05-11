// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType discriminator added — see DIVERGENCE.md.
// Phase 15 Plan 15-07 Wave 3 (Sub-step F option (a)): default flipped 'series' -> 'manga' since
// TV is gone post-cutover; the type union 'series' | 'manga' collapsed to 'manga' (preserves the
// discriminator type for v2 reintroduction). The /history URL branch is dead code now but the
// conditional is left in place so v2 can flip the union back without re-deriving the URL switch.
//
// GH issue #73 (2026-05-11) — type parameter retyped from legacy `History` (TV)
// to `ChapterHistory` so the records emitted by `usePagedApiQuery` carry the
// manga wire shape (mangaId / chapterId / translatedLanguage / scanlationGroup +
// hydrated manga/chapter subresources) consumed by the rewritten HistoryRow.
import { keepPreviousData, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo, useState } from 'react';
import { Filter, FilterBuilderProp } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import usePage from 'Helpers/Hooks/usePage';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { filterBuilderValueTypes } from 'Helpers/Props';
import ChapterHistory from 'typings/ChapterHistory';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import translate from 'Utilities/String/translate';
import { useHistoryOptions } from './historyOptionsStore';

export type HistoryMediaType = 'manga';

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

export const FILTER_BUILDER: FilterBuilderProp<ChapterHistory>[] = [
  {
    name: 'eventType',
    label: () => translate('EventType'),
    type: 'equal',
    valueType: filterBuilderValueTypes.HISTORY_EVENT_TYPE,
  },
  {
    name: 'mangaIds',
    label: () => translate('MangaTitle'),
    type: 'equal',
    valueType: filterBuilderValueTypes.SERIES,
  },
];

type HistoryType = 'episode' | 'series';

const MARK_AS_FAILED_QUERY_KEYS: Record<HistoryType, string> = {
  episode: '/history/episode',
  series: '/history/series',
} as const;

const useHistory = (mediaType: HistoryMediaType = 'manga') => {
  const { page, goToPage } = usePage('history');
  const { pageSize, selectedFilterKey, sortKey, sortDirection } =
    useHistoryOptions();
  const customFilters = useCustomFiltersList('history');

  const filters = useMemo(() => {
    return findSelectedFilters(selectedFilterKey, FILTERS, customFilters);
  }, [selectedFilterKey, customFilters]);

  const path = mediaType === 'manga' ? '/manga/history' : '/history';

  const { refetch, ...query } = usePagedApiQuery<ChapterHistory>({
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
