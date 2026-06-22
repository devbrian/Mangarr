// Phase 42 Plan 42-06 — Discovery filter-drawer tristate chip (NEW-in-Mangarr;
// deliberate divergence from Sonarr's FilterModal/FilterBuilder — see DIVERGENCE.md).
//
// Sketch 001 (LOCKED) chip lifecycle: neutral `+` -> include (green check) ->
// exclude (red ✕, strikethrough label) -> reset (neutral). The parent owns the
// state transition; this component renders the current visual and calls onCycle
// on click. Selections map 1:1 onto MangaBaka's include/exclude param pairs
// (genre/genre_not, type/type_not, …) — D-03 / spike-004.
import classNames from 'classnames';
import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import styles from './TristateChip.css';

export type TristateChipState = 'neutral' | 'include' | 'exclude';

export interface TristateChipProps {
  label: string;
  state: TristateChipState;
  onCycle: (state: TristateChipState) => void;
  'data-testid'?: string;
}

const STATE_ICONS = {
  neutral: icons.ADD,
  include: icons.CHECK,
  exclude: icons.CLOSE,
} as const;

function TristateChip({
  label,
  state,
  onCycle,
  'data-testid': dataTestId,
}: TristateChipProps) {
  const handleClick = useCallback(() => {
    onCycle(state);
  }, [onCycle, state]);

  const icon = STATE_ICONS[state];

  return (
    <button
      type="button"
      className={classNames(
        styles.chip,
        state === 'include' && styles.include,
        state === 'exclude' && styles.exclude
      )}
      aria-pressed={state !== 'neutral'}
      data-state={state}
      data-testid={dataTestId}
      onClick={handleClick}
    >
      <Icon className={styles.icon} name={icon} size={11} />
      <span className={styles.label}>{label}</span>
    </button>
  );
}

export default TristateChip;
