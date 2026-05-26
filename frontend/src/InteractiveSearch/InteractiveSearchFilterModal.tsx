import React, { useCallback } from 'react';
import { SetFilter } from 'Components/Filter/Filter';
import FilterModal, { FilterModalProps } from 'Components/Filter/FilterModal';
import InteractiveSearchPayload from './InteractiveSearchPayload';
import { setReleaseOption } from './releaseOptionsStore';
import useReleases, { FILTER_BUILDER, Release } from './useReleases';

interface InteractiveSearchFilterModalProps extends FilterModalProps<Release> {
  searchPayload: InteractiveSearchPayload;
}

export default function InteractiveSearchFilterModal({
  searchPayload,
  ...otherProps
}: InteractiveSearchFilterModalProps) {
  const { data } = useReleases(searchPayload);

  // Write the filter slot from searchPayload.kind — the same source useReleases
  // reads from — so read/write paths can't diverge.
  const handleFilterSelect = useCallback(
    (selectedFilter: SetFilter) => {
      if (searchPayload.kind === 'chapter') {
        setReleaseOption(
          'chapterSelectedFilterKey',
          selectedFilter.selectedFilterKey
        );
      } else {
        setReleaseOption(
          'mangaSelectedFilterKey',
          selectedFilter.selectedFilterKey
        );
      }
    },
    [searchPayload.kind]
  );

  return (
    <FilterModal
      {...otherProps}
      sectionItems={data}
      filterBuilderProps={FILTER_BUILDER}
      customFilterType="releases"
      dispatchSetFilter={handleFilterSelect}
    />
  );
}
