import classNames from 'classnames';
import React, {
  ChangeEvent,
  ComponentProps,
  SyntheticEvent,
  useCallback,
} from 'react';
import { InputChanged } from 'typings/inputs';
import styles from './SelectInput.css';

export interface SelectInputOption
  extends Pick<ComponentProps<'option'>, 'disabled'> {
  key: string | number;
  value: string | number | (() => string | number);
}

interface SelectInputProps<T> {
  className?: string;
  disabledClassName?: string;
  name: string;
  value: string | number;
  values: SelectInputOption[];
  isDisabled?: boolean;
  hasError?: boolean;
  hasWarning?: boolean;
  autoFocus?: boolean;
  // Phase 18 Plan-04 wrapper sweep: `data-testid` propagation to the underlying
  // <select> element. See data-testid-spec.md §"Wrapper-Component Sweep Ledger".
  'data-testid'?: string;
  onChange: (change: InputChanged<T>) => void;
  onBlur?: (event: SyntheticEvent) => void;
}

function SelectInput<T>({
  className = styles.select,
  disabledClassName = styles.isDisabled,
  name,
  value,
  values,
  isDisabled = false,
  hasError,
  hasWarning,
  autoFocus = false,
  'data-testid': dataTestId,
  onBlur,
  onChange,
}: SelectInputProps<T>) {
  const handleChange = useCallback(
    (event: ChangeEvent<HTMLSelectElement>) => {
      onChange({
        name,
        value: event.target.value as T,
      });
    },
    [name, onChange]
  );

  return (
    <select
      className={classNames(
        className,
        hasError && styles.hasError,
        hasWarning && styles.hasWarning,
        isDisabled && disabledClassName
      )}
      disabled={isDisabled}
      name={name}
      value={value}
      autoFocus={autoFocus}
      data-testid={dataTestId}
      onChange={handleChange}
      onBlur={onBlur}
    >
      {values.map((option) => {
        const { key, value: optionValue, ...otherOptionProps } = option;

        return (
          <option key={key} value={key} {...otherOptionProps}>
            {typeof optionValue === 'function' ? optionValue() : optionValue}
          </option>
        );
      })}
    </select>
  );
}

export default SelectInput;
