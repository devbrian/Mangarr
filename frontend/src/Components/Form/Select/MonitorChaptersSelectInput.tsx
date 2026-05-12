// Sonarr divergence: Phase 17.3 Plan 17.3-04 (D-07) — file renamed from
// MonitorEpisodesSelectInput.tsx to MonitorChaptersSelectInput.tsx; component
// + props interface renamed.
// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// 'Utilities/Series/monitorOptions' rewritten to 'Utilities/Manga/monitorOptions'
// peer (authored 17.3-13b as no-op empty array per Phase 15 Plan 15-12 STUB shape).
// The eventual 5-value MangaMonitor set (all/future/missing/latest/none per
// Manga.ts:27-32) is produced upstream by the monitorOptions source.
import React from 'react';
import monitorOptions from 'Utilities/Manga/monitorOptions';
import translate from 'Utilities/String/translate';
import EnhancedSelectInput, {
  EnhancedSelectInputProps,
  EnhancedSelectInputValue,
} from './EnhancedSelectInput';

export interface MonitorChaptersSelectInputProps
  extends Omit<
    EnhancedSelectInputProps<EnhancedSelectInputValue<string>, string>,
    'values'
  > {
  includeNoChange?: boolean;
  includeMixed?: boolean;
}

function MonitorChaptersSelectInput(props: MonitorChaptersSelectInputProps) {
  const {
    includeNoChange = false,
    includeMixed = false,
    ...otherProps
  } = props;

  const values: EnhancedSelectInputValue<string>[] = [...monitorOptions];

  if (includeNoChange) {
    values.unshift({
      key: 'noChange',
      get value() {
        return translate('NoChange');
      },
      isDisabled: true,
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

  return <EnhancedSelectInput {...otherProps} values={values} />;
}

export default MonitorChaptersSelectInput;
