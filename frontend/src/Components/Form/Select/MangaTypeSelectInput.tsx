import React, { useMemo } from 'react';
// Sonarr divergence: Phase 17.3 Plan 17.3-04 (D-07) — file renamed from
// SeriesTypeSelectInput.tsx to MangaTypeSelectInput.tsx; options array rebuilt
// to MangaType ('manga' | 'manhwa' | 'manhua' | 'oneshot') per Manga.ts:47.
// The 'Utilities/Series/seriesTypes' import is intentionally left untouched
// here so Plan 17.3-12 (D-09/D-10 stub cascade) owns its rewrite to
// Utilities/Manga/mangaTypes. The options array no longer references the
// seriesTypes constants (string-literal keys per MangaType union).
import translate from 'Utilities/String/translate';
import EnhancedSelectInput, {
  EnhancedSelectInputProps,
  EnhancedSelectInputValue,
} from './EnhancedSelectInput';
import MangaTypeSelectInputOption from './MangaTypeSelectInputOption';
import MangaTypeSelectInputSelectedValue from './MangaTypeSelectInputSelectedValue';

export interface MangaTypeSelectInputProps
  extends Omit<
    EnhancedSelectInputProps<EnhancedSelectInputValue<string>, string>,
    'values'
  > {
  includeNoChange?: boolean;
  includeNoChangeDisabled?: boolean;
  includeMixed?: boolean;
}

export interface IMangaTypeOption {
  key: string;
  value: string;
  format?: string;
  isDisabled?: boolean;
}

const mangaTypeOptions: IMangaTypeOption[] = [
  {
    key: 'manga',
    value: 'Manga',
    format: 'Right-to-Left',
  },
  {
    key: 'manhwa',
    value: 'Manhwa',
    format: 'Top-to-Bottom (Korean)',
  },
  {
    key: 'manhua',
    value: 'Manhua',
    format: 'Top-to-Bottom (Chinese)',
  },
  {
    key: 'oneshot',
    value: 'Oneshot',
    format: 'Single chapter',
  },
];

function MangaTypeSelectInput(props: MangaTypeSelectInputProps) {
  const {
    includeNoChange = false,
    includeNoChangeDisabled = true,
    includeMixed = false,
  } = props;

  const values = useMemo(() => {
    const result = [...mangaTypeOptions];

    if (includeNoChange) {
      result.unshift({
        key: 'noChange',
        value: translate('NoChange'),
        isDisabled: includeNoChangeDisabled,
      });
    }

    if (includeMixed) {
      result.unshift({
        key: 'mixed',
        value: `(${translate('Mixed')})`,
        isDisabled: true,
      });
    }

    return result;
  }, [includeNoChange, includeNoChangeDisabled, includeMixed]);

  return (
    <EnhancedSelectInput
      {...props}
      values={values}
      optionComponent={MangaTypeSelectInputOption}
      selectedValueComponent={MangaTypeSelectInputSelectedValue}
    />
  );
}

export default MangaTypeSelectInput;
