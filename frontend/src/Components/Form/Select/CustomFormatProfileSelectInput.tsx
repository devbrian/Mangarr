// quick-260608-vf9 follow-up — CustomFormatProfile peer of
// TranslationProfileSelectInput.tsx. Wires useCustomFormatProfilesData()
// against /api/v5/customformatprofile so the import-list (and any future
// bulk-edit) form can pick a CustomFormatProfile. Unlike the translation
// select (which auto-selects the first profile), this defaults to the
// profile flagged isDefault — falling back to the first — so a fresh import
// list lands on the user's default custom format.
import React, { useCallback, useEffect, useMemo } from 'react';
import { useCustomFormatProfilesData } from 'Settings/Profiles/CustomFormatProfile/useCustomFormatProfiles';
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
  const customFormatProfiles = useCustomFormatProfilesData();

  return useMemo(() => {
    const values: EnhancedSelectInputValue<number | string>[] =
      customFormatProfiles
        .slice()
        .sort(sortByProp('name'))
        .map((customFormatProfile) => {
          return {
            key: customFormatProfile.id,
            value: customFormatProfile.name,
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
    customFormatProfiles,
    includeNoChange,
    includeNoChangeDisabled,
    includeMixed,
  ]);
};

export interface CustomFormatProfileSelectInputProps
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

function CustomFormatProfileSelectInput({
  name,
  value,
  includeNoChange = false,
  includeNoChangeDisabled = true,
  includeMixed = false,
  onChange,
  ...otherProps
}: CustomFormatProfileSelectInputProps) {
  const customFormatProfiles = useCustomFormatProfilesData();
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
    if (!value || !values.some((option) => option.key === value)) {
      // Default to the profile flagged isDefault, falling back to the first
      // numeric-keyed profile so a fresh import list lands on the user's
      // default custom format rather than a blank/0 (orphan FK) selection.
      const defaultProfile =
        customFormatProfiles.find((profile) => profile.isDefault) ??
        customFormatProfiles
          .slice()
          .sort(sortByProp('name'))
          .find((profile) => typeof profile.id === 'number');

      if (defaultProfile) {
        onChange({ name, value: defaultProfile.id });
      }
    }
  }, [name, value, values, customFormatProfiles, onChange]);

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

export default CustomFormatProfileSelectInput;
