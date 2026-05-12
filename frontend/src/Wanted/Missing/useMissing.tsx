// Sonarr divergence: per Phase 7 D-10 + Lock #1 — mediaType discriminator added — see DIVERGENCE.md.
// Phase 15 Plan 15-07 Wave 3 (Sub-step F option (a)): default flipped 'series' -> 'manga' since
// TV is gone post-cutover; the type union 'series' | 'manga' collapsed to 'manga' (preserves the
// discriminator type for v2 reintroduction). The /wanted/missing URL branch is dead code now but
// the conditional is left in place so v2 can flip the union back without re-deriving the URL switch.
// Sonarr divergence: Phase 17.3 Plan 17.3-13 (D-09 stub-importer cascade) — Episode/* stub
// imports rewritten to Chapter/* peers. The Phase 15 ~247-error cascade closed by Plan 17.3-13
// (Wanted/* branch); the Phase-15 historical NOTE above is preserved as repo memory of the
// pre-rename state.
import { keepPreviousData } from '@tanstack/react-query';
import { useEffect, useMemo } from 'react';
import Chapter from 'Chapter/Chapter';
import { setChapterQueryKey } from 'Chapter/useChapter';
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

export const FILTER_BUILDER: FilterBuilderProp<Chapter>[] = [
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

  const { isPlaceholderData, queryKey, ...query } = usePagedApiQuery<Chapter>({
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
      setChapterQueryKey('wanted.missing', queryKey);
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
