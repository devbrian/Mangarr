// Sonarr divergence: Phase 17.3 Plan 17.3-13 (D-09 stub-importer cascade) —
// Episode/Episode rewritten to Chapter/Chapter peer.
import React, { useCallback } from 'react';
import Chapter from 'Chapter/Chapter';
import { SetFilter } from 'Components/Filter/Filter';
import FilterModal, { FilterModalProps } from 'Components/Filter/FilterModal';
import { setCutoffUnmetOption } from './cutoffUnmetOptionsStore';
import useCutoffUnmet, { FILTER_BUILDER } from './useCutoffUnmet';

type CutoffUnmetFilterModalProps = FilterModalProps<Chapter>;

export default function CutoffUnmetFilterModal(
  props: CutoffUnmetFilterModalProps
) {
  const { records } = useCutoffUnmet();

  const dispatchSetFilter = useCallback(({ selectedFilterKey }: SetFilter) => {
    setCutoffUnmetOption('selectedFilterKey', selectedFilterKey);
  }, []);

  return (
    <FilterModal
      {...props}
      sectionItems={records}
      filterBuilderProps={FILTER_BUILDER}
      customFilterType="wanted.cutoffUnmet"
      dispatchSetFilter={dispatchSetFilter}
    />
  );
}
