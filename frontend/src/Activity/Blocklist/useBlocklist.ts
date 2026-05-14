// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType discriminator added — see DIVERGENCE.md.
// Phase 15 Plan 15-07 Wave 3 (Sub-step F option (a)): default flipped 'series' -> 'manga' since
// TV is gone post-cutover; the type union 'series' | 'manga' collapsed to 'manga' (preserves the
// discriminator type for v2 reintroduction — v2 may readd 'series' or rename if expanding the
// catalogue beyond manga). The /blocklist URL branch is dead code now but the conditional is left
// in place so v2 can flip the union back without re-deriving the URL switch.
//
// GH issue #73 (2026-05-11) — type parameter retyped from legacy `Blocklist` (TV)
// to `MangaBlocklist` so the records emitted by `usePagedApiQuery` carry the
// manga wire shape (mangaId / chapterIds / translatedLanguage / scanlationGroup +
// optional hydrated manga subresource) consumed by the rewritten BlocklistRow.
import { keepPreviousData, useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import { Filter, FilterBuilderProp } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import usePage from 'Helpers/Hooks/usePage';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { filterBuilderValueTypes } from 'Helpers/Props';
import MangaBlocklist from 'typings/MangaBlocklist';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import translate from 'Utilities/String/translate';
import { useBlocklistOptions } from './blocklistOptionsStore';

export type BlocklistMediaType = 'manga';

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

export const FILTER_BUILDER: FilterBuilderProp<MangaBlocklist>[] = [
  {
    name: 'mangaIds',
    label: () => translate('MangaTitle'),
    type: 'equal',
    valueType: filterBuilderValueTypes.SERIES,
  },
];

const useBlocklist = (mediaType: BlocklistMediaType = 'manga') => {
  const { page, goToPage } = usePage('blocklist');
  const { pageSize, selectedFilterKey, sortKey, sortDirection } =
    useBlocklistOptions();
  const customFilters = useCustomFiltersList('blocklist');

  const filters = useMemo(() => {
    return findSelectedFilters(selectedFilterKey, FILTERS, customFilters);
  }, [selectedFilterKey, customFilters]);

  const path = mediaType === 'manga' ? '/manga/blocklist' : '/blocklist';

  const { refetch, ...query } = usePagedApiQuery<MangaBlocklist>({
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

// Phase 19 Plan 19-05 (Rule 1 fix-forward): the GH #73 migration repointed
// the blocklist GET hook (`useBlocklist`) onto the `/manga/blocklist`
// endpoint but left both DELETE hooks below pointing at the dead TV
// `/blocklist` routes — the per-row DELETE 404'd and the bulk DELETE 405'd
// (`/api/v5/blocklist/bulk` has no manga peer; the manga controller is
// `[V5ApiController("manga/blocklist")]`). Repointed to `/manga/blocklist/*`
// and the invalidation key fixed to `['/manga/blocklist']` so a successful
// remove actually refreshes the manga blocklist view.
export const useRemoveBlocklistItem = (id: number) => {
  const queryClient = useQueryClient();

  const { mutate, isPending } = useApiMutation<unknown, void>({
    path: `/manga/blocklist/${id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/manga/blocklist'] });
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
    path: `/manga/blocklist/bulk`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/manga/blocklist'] });
      },
    },
  });

  return {
    removeBlocklistItems: mutate,
    isRemoving: isPending,
  };
};
