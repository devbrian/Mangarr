// Sonarr divergence: Phase 17.3 Plan 17.3-13 (D-09 stub-importer cascade) —
// Episode/Episode rewritten to Chapter/Chapter peer.
import React, { useCallback } from 'react';
import Chapter from 'Chapter/Chapter';
import { SetFilter } from 'Components/Filter/Filter';
import FilterModal, { FilterModalProps } from 'Components/Filter/FilterModal';
import { setMissingOption } from './missingOptionsStore';
import useMissing, { FILTER_BUILDER } from './useMissing';

type MissingFilterModalProps = FilterModalProps<Chapter>;

export default function MissingFilterModal(props: MissingFilterModalProps) {
  const { records } = useMissing();

  const dispatchSetFilter = useCallback(({ selectedFilterKey }: SetFilter) => {
    setMissingOption('selectedFilterKey', selectedFilterKey);
  }, []);

  return (
    <FilterModal
      {...props}
      sectionItems={records}
      filterBuilderProps={FILTER_BUILDER}
      customFilterType="wanted.missing"
      dispatchSetFilter={dispatchSetFilter}
    />
  );
}
