// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType discriminator added — see DIVERGENCE.md.
// Default 'series' preserves all existing TV call-sites unchanged. Manga callers pass 'manga'
// (currently the MangaBlocklist thin wrapper at /manga/activity/blocklist) which switches the URL
// to /manga/blocklist and the React Query key to ['/manga/blocklist'] so SignalR invalidations
// namespace cleanly per Plan 07-02 (Pitfall 5 — TV/manga cache MUST NOT collide). Phase 8 collapses.
import { keepPreviousData, useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import { Filter, FilterBuilderProp } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import usePage from 'Helpers/Hooks/usePage';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { filterBuilderValueTypes } from 'Helpers/Props';
import Blocklist from 'typings/Blocklist';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import translate from 'Utilities/String/translate';
import { useBlocklistOptions } from './blocklistOptionsStore';

export type BlocklistMediaType = 'series' | 'manga';

interface BulkBlocklistData {
  ids: number[];
}

export const FILTERS: Filter[] = [
  {
    key: 'all',
    label: () => translate('All'),
    filters: [],
  },
];

export const FILTER_BUILDER: FilterBuilderProp<Blocklist>[] = [
  {
    name: 'seriesIds',
    label: () => translate('Series'),
    type: 'equal',
    valueType: filterBuilderValueTypes.SERIES,
  },
  {
    name: 'protocols',
    label: () => translate('Protocol'),
    type: 'equal',
    valueType: filterBuilderValueTypes.PROTOCOL,
  },
];

const useBlocklist = (mediaType: BlocklistMediaType = 'series') => {
  const { page, goToPage } = usePage('blocklist');
  const { pageSize, selectedFilterKey, sortKey, sortDirection } =
    useBlocklistOptions();
  const customFilters = useCustomFiltersList('blocklist');

  const filters = useMemo(() => {
    return findSelectedFilters(selectedFilterKey, FILTERS, customFilters);
  }, [selectedFilterKey, customFilters]);

  const path = mediaType === 'manga' ? '/manga/blocklist' : '/blocklist';

  const { refetch, ...query } = usePagedApiQuery<Blocklist>({
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

export default useBlocklist;

export const useFilters = () => {
  return FILTERS;
};

export const useRemoveBlocklistItem = (id: number) => {
  const queryClient = useQueryClient();

  const { mutate, isPending } = useApiMutation<unknown, void>({
    path: `/blocklist/${id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/blocklist'] });
      },
    },
  });

  return {
    removeBlocklistItem: mutate,
    isRemoving: isPending,
  };
};

export const useRemoveBlocklistItems = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending } = useApiMutation<unknown, BulkBlocklistData>({
    path: `/blocklist/bulk`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/blocklist'] });
      },
    },
  });

  return {
    removeBlocklistItems: mutate,
    isRemoving: isPending,
  };
};
