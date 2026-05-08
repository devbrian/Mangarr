// Sonarr divergence: per Phase 7 D-10 + Lock #1 (Plan 12-08 sub-wave C extension) — mediaType discriminator added — see DIVERGENCE.md.
// Phase 15 Plan 15-07 Wave 3 (Sub-step F option (a)): default flipped 'series' -> 'manga' since
// TV is gone post-cutover; the type union 'series' | 'manga' collapsed to 'manga' (preserves the
// discriminator type for v2 reintroduction). The /wanted/cutoff URL branch is dead code now but
// the conditional is left in place so v2 can flip the union back without re-deriving the URL switch.
// NOTE: Episode imports below are orphaned post-Plan 15-07 Task 1 (frontend/src/Episode/ deleted)
// and contribute to the expected ~247-error TS2307 cascade — Plan 15-08/15-09 resolves this.
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

export type CutoffUnmetMediaType = 'manga';

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

const useCutoffUnmet = (mediaType: CutoffUnmetMediaType = 'manga') => {
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
