import classNames from 'classnames';
import React, { SyntheticEvent, useCallback, useEffect, useRef } from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import { Kind } from 'Helpers/Props/kinds';
import { CheckInputChanged } from 'typings/inputs';
import FormInputHelpText from './FormInputHelpText';
import styles from './CheckInput.css';

interface ChangeEvent<T = Element> extends SyntheticEvent<T, MouseEvent> {
  target: EventTarget & T;
}

export interface CheckInputProps {
  className?: string;
  containerClassName?: string;
  name: string;
  checkedValue?: boolean;
  uncheckedValue?: boolean;
  value?: string | boolean | null;
  helpText?: string;
  helpTextWarning?: string;
  isDisabled?: boolean;
  kind?: Extract<Kind, keyof typeof styles>;
  // Phase 18 Plan-04 wrapper sweep + GH #180 scope A: `data-testid`
  // propagation. Lands on the wrapping <label> (the visible click target)
  // because CheckInput renders the <input type="checkbox"> visually hidden
  // behind a <div> visual surrogate that intercepts pointer events. The
  // label is what Playwright fixtures interact with, so the testid must
  // live there. Mirrors the `'data-testid'?: string` convention from
  // TextInput.tsx:32. See data-testid-spec.md §"Wrapper-Component Sweep
  // Ledger".
  'data-testid'?: string;
  onChange: (changes: CheckInputChanged) => void;
}

function CheckInput(props: CheckInputProps) {
  const {
    className = styles.input,
    containerClassName = styles.container,
    name,
    value,
    checkedValue = true,
    uncheckedValue = false,
    helpText,
    helpTextWarning,
    isDisabled,
    kind = 'primary',
    'data-testid': dataTestId,
    onChange,
  } = props;

  const inputRef = useRef<HTMLInputElement>(null);

  const isChecked = value === checkedValue;
  const isUnchecked = value === uncheckedValue;
  const isIndeterminate = !isChecked && !isUnchecked;

  const toggleChecked = useCallback(
    (checked: boolean, shiftKey: boolean) => {
      const newValue = checked ? checkedValue : uncheckedValue;

      if (value !== newValue) {
        onChange({
          name,
          value: newValue,
          shiftKey,
        });
      }
    },
    [name, value, checkedValue, uncheckedValue, onChange]
  );

  const handleClick = useCallback(
    (event: SyntheticEvent<HTMLElement, MouseEvent>) => {
      if (isDisabled) {
        return;
      }

      const shiftKey = event.nativeEvent.shiftKey;
      const checked = !(inputRef.current?.checked ?? false);

      event.preventDefault();
      toggleChecked(checked, shiftKey);
    },
    [isDisabled, toggleChecked]
  );

  const handleChange = useCallback(
    (event: ChangeEvent<HTMLInputElement>) => {
      const checked = event.target.checked;
      const shiftKey = event.nativeEvent.shiftKey;

      toggleChecked(checked, shiftKey);
    },
    [toggleChecked]
  );

  useEffect(() => {
    if (!inputRef.current) {
      return;
    }

    inputRef.current.indeterminate =
      value !== uncheckedValue && value !== checkedValue;
  }, [value, uncheckedValue, checkedValue]);

  return (
    <div className={containerClassName}>
      {/*
        a11y (GH #254): the keyboard-accessible control is the nested native
        <input type="checkbox"> (Tab + Space toggles it). The label onClick is
        a mouse-only affordance, so no keyboard listener is required here.
      */}
      {/* eslint-disable-next-line jsx-a11y/click-events-have-key-events */}
      <label
        className={styles.label}
        data-testid={dataTestId}
        onClick={handleClick}
      >
        <input
          ref={inputRef}
          className={styles.checkbox}
          type="checkbox"
          name={name}
          checked={isChecked}
          disabled={isDisabled}
          onChange={handleChange}
        />

        <div
          className={classNames(
            className,
            isChecked ? styles[kind] : styles.isNotChecked,
            isIndeterminate && styles.isIndeterminate,
            isDisabled && styles.isDisabled
          )}
        >
          {isChecked ? <Icon name={icons.CHECK} /> : null}

          {isIndeterminate ? <Icon name={icons.CHECK_INDETERMINATE} /> : null}
        </div>

        {helpText ? (
          <FormInputHelpText className={styles.helpText} text={helpText} />
        ) : null}

        {!helpText && helpTextWarning ? (
          <FormInputHelpText
            className={styles.helpText}
            text={helpTextWarning}
            isWarning={true}
          />
        ) : null}
      </label>
    </div>
  );
}

export default CheckInput;
