// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType discriminator added — see DIVERGENCE.md.
// Default 'series' preserves all existing TV call-sites unchanged. Manga callers pass 'manga'
// (currently the MangaMissing thin wrapper at /manga/wanted/missing) which switches the URL to
// /manga/wanted/missing and the React Query key (auto-derived from path arg in usePagedApiQuery)
// to ['/manga/wanted/missing'] so SignalR invalidations namespace cleanly per Plan 07-02
// (Pitfall 5 — TV/manga cache MUST NOT collide). Phase 8 collapses.
import { keepPreviousData } from '@tanstack/react-query';
import { useEffect, useMemo } from 'react';
import Episode from 'Episode/Episode';
import { setEpisodeQueryKey } from 'Episode/useEpisode';
import { Filter, FilterBuilderProp } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import usePage from 'Helpers/Hooks/usePage';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { filterBuilderValueTypes } from 'Helpers/Props';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import translate from 'Utilities/String/translate';
import { useMissingOptions } from './missingOptionsStore';

export type MissingMediaType = 'series' | 'manga';

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
  {
    key: 'excludeSpecials',
    label: () => translate('ExcludeSpecials'),
    filters: [
      {
        key: 'includeSpecials',
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
  {
    name: 'includeSpecials',
    label: () => translate('IncludeSpecials'),
    type: 'equal',
    valueType: filterBuilderValueTypes.BOOL,
  },
];

const useMissing = (mediaType: MissingMediaType = 'series') => {
  const { page, goToPage } = usePage('missing');
  const { pageSize, selectedFilterKey, sortKey, sortDirection } =
    useMissingOptions();
  const customFilters = useCustomFiltersList('wanted.missing');

  const filters = useMemo(() => {
    return findSelectedFilters(selectedFilterKey, FILTERS, customFilters);
  }, [selectedFilterKey, customFilters]);

  const path =
    mediaType === 'manga' ? '/manga/wanted/missing' : '/wanted/missing';

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
      setEpisodeQueryKey('wanted.missing', queryKey);
    }
  }, [isPlaceholderData, queryKey]);

  return {
    ...query,
    goToPage,
    isPlaceholderData,
    page,
  };
};

export default useMissing;

export const useFilters = () => {
  return FILTERS;
};
