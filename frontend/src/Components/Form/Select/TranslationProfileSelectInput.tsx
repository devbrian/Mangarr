// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Components/Form/Select/QualityProfileSelectInput.tsx.
//
// Phase 17 follow-up (debug `qualityprofiles-redux-rename`, 2026-05-12 — GH #82
// Path 1 surface-rename cascade): the inherited `QualityProfileSelectInput`
// fired `GET /api/v5/qualityprofile` (404 — endpoint deleted in Phase 15 Plan
// 15-03 D-12). This translation-profile sibling wires
// `useTranslationProfilesData()` against `/api/v5/translationprofile` (Phase 5
// Plan 05-02 canonical replacement) and is consumed by the bulk Edit Manga
// modal's translation-profile select. The legacy `QualityProfileSelectInput`
// file is preserved on disk for the hidden Settings/Profiles/Quality page
// (per Pitfall 8 negative gate — Phase 8 deletes the Quality sub-tree).
import React, { useCallback, useEffect, useMemo } from 'react';
import { useTranslationProfilesData } from 'Settings/Profiles/Translations/useTranslationProfiles';
import { EnhancedSelectInputChanged } from 'typings/inputs';
import sortByProp from 'Utilities/Array/sortByProp';
import translate from 'Utilities/String/translate';
import EnhancedSelectInput, {
  EnhancedSelectInputProps,
  EnhancedSelectInputValue,
} from './EnhancedSelectInput';

const useValues = (
  includeNoChange: boolean,
  includeNoChangeDisabled: boolean,
  includeMixed: boolean
) => {
  const translationProfiles = useTranslationProfilesData();

  return useMemo(() => {
    const values: EnhancedSelectInputValue<number | string>[] =
      translationProfiles
        .slice()
        .sort(sortByProp('name'))
        .map((translationProfile) => {
          return {
            key: translationProfile.id,
            value: translationProfile.name,
          };
        });

    if (includeNoChange) {
      values.unshift({
        key: 'noChange',
        get value() {
          return translate('NoChange');
        },
        isDisabled: includeNoChangeDisabled,
      });
    }

    if (includeMixed) {
      values.unshift({
        key: 'mixed',
        get value() {
          return `(${translate('Mixed')})`;
        },
        isDisabled: true,
      });
    }

    return values;
  }, [
    translationProfiles,
    includeNoChange,
    includeNoChangeDisabled,
    includeMixed,
  ]);
};

export interface TranslationProfileSelectInputProps
  extends Omit<
    EnhancedSelectInputProps<
      EnhancedSelectInputValue<number | string>,
      number | string
    >,
    'values'
  > {
  name: string;
  includeNoChange?: boolean;
  includeNoChangeDisabled?: boolean;
  includeMixed?: boolean;
}

function TranslationProfileSelectInput({
  name,
  value,
  includeNoChange = false,
  includeNoChangeDisabled = true,
  includeMixed = false,
  onChange,
  ...otherProps
}: TranslationProfileSelectInputProps) {
  const values = useValues(
    includeNoChange,
    includeNoChangeDisabled,
    includeMixed
  );

  const handleChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string | number>) => {
      onChange({ name, value });
    },
    [name, onChange]
  );

  useEffect(() => {
    if (
      !value ||
      !values.some((option) => option.key === value || option.key === value)
    ) {
      const firstValue = values.find(
        (option) => typeof option.key === 'number'
      );

      if (firstValue) {
        onChange({ name, value: firstValue.key });
      }
    }
  }, [name, value, values, onChange]);

  return (
    <EnhancedSelectInput
      {...otherProps}
      name={name}
      value={value}
      values={values}
      onChange={handleChange}
    />
  );
}

export default TranslationProfileSelectInput;
