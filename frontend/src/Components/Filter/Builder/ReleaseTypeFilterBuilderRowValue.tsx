import React from 'react';
import translate from 'Utilities/String/translate';
import FilterBuilderRowValue, {
  FilterBuilderRowValueProps,
} from './FilterBuilderRowValue';

const releaseTypeList = [
  {
    id: 'unknown',
    get name() {
      return translate('Unknown');
    },
  },
  {
    id: 'singleEpisode',
    get name() {
      return translate('SingleChapter');
    },
  },
  {
    id: 'multiEpisode',
    get name() {
      return translate('MultiChapter');
    },
  },
  {
    id: 'seasonPack',
    get name() {
      return translate('SeasonPack');
    },
  },
];

type ReleaseTypeFilterBuilderRowValueProps<T> = Omit<
  FilterBuilderRowValueProps<T, string, string>,
  'tagList'
>;

function ReleaseTypeFilterBuilderRowValue<T>(
  props: ReleaseTypeFilterBuilderRowValueProps<T>
) {
  return <FilterBuilderRowValue tagList={releaseTypeList} {...props} />;
}

export default ReleaseTypeFilterBuilderRowValue;
