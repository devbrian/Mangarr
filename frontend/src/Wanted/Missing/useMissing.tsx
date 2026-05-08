// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType discriminator added — see DIVERGENCE.md.
// Phase 15 Plan 15-07 Wave 3 (Sub-step F option (a)): default flipped 'series' -> 'manga' since
// TV is gone post-cutover; the type union 'series' | 'manga' collapsed to 'manga' (preserves the
// discriminator type for v2 reintroduction). The /wanted/missing URL branch is dead code now but
// the conditional is left in place so v2 can flip the union back without re-deriving the URL switch.
// NOTE: Episode imports below are orphaned post-Plan 15-07 Task 1 (frontend/src/Episode/ deleted)
// and contribute to the expected ~247-error TS2307 cascade — Plan 15-08/15-09 resolves this.
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

export type MissingMediaType = 'manga';

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

const useMissing = (mediaType: MissingMediaType = 'manga') => {
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
