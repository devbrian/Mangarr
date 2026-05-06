// Sonarr divergence: per Phase 7 D-10 + Lock #1 (Plan 12-08 sub-wave C extension) — mediaType discriminator added — see DIVERGENCE.md.
// Default 'series' preserves all existing TV call-sites unchanged. Manga callers pass 'manga'
// (currently the MangaCutoffUnmet thin wrapper at /manga/wanted/cutoffunmet) which switches the URL to
// /manga/wanted/cutoff and the React Query key (auto-derived from path arg in usePagedApiQuery)
// to ['/manga/wanted/cutoff'] so SignalR invalidations namespace cleanly per Plan 07-02
// (Pitfall 5 — TV/manga cache MUST NOT collide). Phase 8 collapses.
//
// Closest analog: frontend/src/Wanted/Missing/useMissing.tsx (Plan 07-10) — same pattern.
//
// Phase 12 REVIEW MED-03 — body now actually mirrors useMissing.tsx:79-96 by composing
// `filters` from `findSelectedFilters(selectedFilterKey, FILTERS, customFilters)` and
// passing it to usePagedApiQuery (replacing the prior `queryParams: { monitored }`
// shortcut). Functionally equivalent on the URL surface — `findSelectedFilters` emits
// `[{key: 'monitored', value: [true|false], type: 'equal'}]`, which getQueryString
// flattens to `?monitored=true|false` (the same shape the controller's `bool monitored`
// query param binds). The structural symmetry matches the docstring's claim and
// the customFilters UI surface is wired via CutoffUnmetFilterModal.tsx (sibling
// to MissingFilterModal.tsx; consumes the FILTER_BUILDER export below).
import { keepPreviousData } from '@tanstack/react-query';
import { useEffect, useMemo } from 'react';
import Episode from 'Episode/Episode';
import { setEpisodeQueryKey } from 'Episode/useEpisode';
import { Filter, FilterBuilderProp } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import { filterBuilderValueTypes } from 'Helpers/Props';
import usePage from 'Helpers/Hooks/usePage';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import translate from 'Utilities/String/translate';
import { useCutoffUnmetOptions } from './cutoffUnmetOptionsStore';

export type CutoffUnmetMediaType = 'series' | 'manga';

export const FILTERS: Filter[] = [
  {
    key: 'monitored',
    label: () => translate('Monitored'),
    filters: [
      {
        key: 'monitored',
        value: [true],
        type: 'equal',
      },
    ],
  },
  {
    key: 'unmonitored',
    label: () => translate('Unmonitored'),
    filters: [
      {
        key: 'monitored',
        value: [false],
        type: 'equal',
      },
    ],
  },
];

export const FILTER_BUILDER: FilterBuilderProp<Episode>[] = [
  {
    name: 'monitored',
    label: () => translate('Monitored'),
    type: 'exact',
    valueType: filterBuilderValueTypes.BOOL,
  },
];

const useCutoffUnmet = (mediaType: CutoffUnmetMediaType = 'series') => {
  const { page, goToPage } = usePage('cutoffUnmet');
  const { pageSize, selectedFilterKey, sortKey, sortDirection } =
    useCutoffUnmetOptions();
  const customFilters = useCustomFiltersList('wanted.cutoffUnmet');

  const filters = useMemo(() => {
    return findSelectedFilters(selectedFilterKey, FILTERS, customFilters);
  }, [selectedFilterKey, customFilters]);

  const path =
    mediaType === 'manga' ? '/manga/wanted/cutoff' : '/wanted/cutoff';

  const { isPlaceholderData, queryKey, ...query } = usePagedApiQuery<Episode>({
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

  useEffect(() => {
    if (!isPlaceholderData) {
      setEpisodeQueryKey('wanted.cutoffUnmet', queryKey);
    }
  }, [isPlaceholderData, queryKey]);

  return {
    ...query,
    goToPage,
    isPlaceholderData,
    page,
  };
};

export default useCutoffUnmet;
