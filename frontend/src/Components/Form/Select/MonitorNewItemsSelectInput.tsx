// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// 'Utilities/Series/monitorNewItemsOptions' rewritten to
// 'Utilities/Manga/monitorNewItemsOptions' peer (authored 17.3-13b as no-op
// empty array per Phase 15 Plan 15-12 STUB shape).
import React from 'react';
import monitorNewItemsOptions from 'Utilities/Manga/monitorNewItemsOptions';
import EnhancedSelectInput, {
  EnhancedSelectInputProps,
  EnhancedSelectInputValue,
} from './EnhancedSelectInput';

export interface MonitorNewItemsSelectInputProps
  extends Omit<
    EnhancedSelectInputProps<EnhancedSelectInputValue<string>, string>,
    'values'
  > {
  includeNoChange?: boolean;
  includeNoChangeDisabled?: boolean;
  includeMixed?: boolean;
}

function MonitorNewItemsSelectInput(props: MonitorNewItemsSelectInputProps) {
  const {
    includeNoChange = false,
    includeNoChangeDisabled = true,
    includeMixed = false,
    ...otherProps
  } = props;

  const values: EnhancedSelectInputValue<string>[] = [
    ...monitorNewItemsOptions,
  ];

  if (includeNoChange) {
    values.unshift({
      key: 'noChange',
      value: 'No Change',
      isDisabled: includeNoChangeDisabled,
    });
  }

  if (includeMixed) {
    values.unshift({
      key: 'mixed',
      value: '(Mixed)',
      isDisabled: true,
    });
  }

  return <EnhancedSelectInput {...otherProps} values={values} />;
}

export default MonitorNewItemsSelectInput;
