// Sonarr divergence: Phase 17.3 Plan 17.3-04 (D-07) — file renamed from
// MonitorEpisodesSelectInput.tsx to MonitorChaptersSelectInput.tsx; component
// + props interface renamed. The 'Utilities/Series/monitorOptions' import is
// intentionally left untouched — Plan 17.3-12 (D-09/D-10 stub cascade) owns
// its rewrite to Utilities/Manga/monitorOptions. The monitor options array is
// sourced from that stub (currently empty []) so no inline TV-shape values
// exist in this file to rebuild; the eventual 5-value MangaMonitor set
// (all/future/missing/latest/none per Manga.ts:33-38) is produced upstream by
// the monitorOptions source.
import React from 'react';
import monitorOptions from 'Utilities/Series/monitorOptions';
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
