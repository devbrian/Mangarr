// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Components/Filter/Builder/QualityProfileFilterBuilderRowValue.tsx.
//
// Phase 17 follow-up (debug `qualityprofiles-redux-rename`, 2026-05-12 — GH #82
// Path 1 surface-rename cascade): the inherited `QualityProfileFilterBuilderRowValue`
// fed off `useQualityProfilesData()` → `/api/v5/qualityprofile` (404 — endpoint
// deleted in Phase 15 Plan 15-03 D-12). This translation-profile sibling reads
// `useTranslationProfilesData()` against `/api/v5/translationprofile` (Phase 5
// Plan 05-02 canonical replacement) and is registered against the new
// `filterBuilderValueTypes.TRANSLATION_PROFILE` token. The legacy
// `QualityProfileFilterBuilderRowValue` file is preserved on disk for any
// custom-filter rows still referencing the legacy token (Phase 8 deletes
// alongside the Quality sub-tree).
import React from 'react';
import { useTranslationProfilesData } from 'Settings/Profiles/Translations/useTranslationProfiles';
import sortByProp from 'Utilities/Array/sortByProp';
import FilterBuilderRowValue, {
  FilterBuilderRowValueProps,
} from './FilterBuilderRowValue';

type TranslationProfileFilterBuilderRowValueProps<T> = Omit<
  FilterBuilderRowValueProps<T, number, string>,
  'tagList'
>;

function TranslationProfileFilterBuilderRowValue<T>(
  props: TranslationProfileFilterBuilderRowValueProps<T>
) {
  const translationProfiles = useTranslationProfilesData();

  const tagList = translationProfiles
    .map(({ id, name }) => ({ id, name }))
    .sort(sortByProp('name'));

  return <FilterBuilderRowValue {...props} tagList={tagList} />;
}

export default TranslationProfileFilterBuilderRowValue;
