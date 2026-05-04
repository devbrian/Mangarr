import React, { useCallback } from 'react';
import FilterModal, { FilterModalProps } from 'Components/Filter/FilterModal';
import Manga from 'Manga/Manga';
import { setMangaOption } from 'Manga/mangaOptionsStore';
import useManga, { FILTER_BUILDER } from 'Manga/useManga';

type MangaIndexFilterModalProps = FilterModalProps<Manga>;

export default function MangaIndexFilterModal(
  props: MangaIndexFilterModalProps
) {
  const { data: sectionItems } = useManga();

  const dispatchSetFilter = useCallback(
    ({ selectedFilterKey }: { selectedFilterKey: string | number }) => {
      setMangaOption('selectedFilterKey', selectedFilterKey);
    },
    []
  );

  return (
    <FilterModal
      {...props}
      sectionItems={sectionItems}
      filterBuilderProps={FILTER_BUILDER}
      customFilterType="manga"
      dispatchSetFilter={dispatchSetFilter}
    />
  );
}
