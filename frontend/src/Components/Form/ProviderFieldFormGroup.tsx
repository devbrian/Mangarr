import React, { useMemo } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { FieldSelectOption } from 'typings/Field';
import { InputChanged } from 'typings/inputs';
import { Failure } from 'typings/pending';

interface ProviderFieldFormGroupProps<T> {
  advancedSettings: boolean;
  name: string;
  label: string;
  helpText?: string;
  helpTextWarning?: string;
  helpLink?: string;
  placeholder?: string;
  value?: T;
  type: string;
  advanced: boolean;
  hidden?: string;
  isDisabled?: boolean;
  provider?: string;
  providerData?: object;
  pending: boolean;
  errors: Failure[];
  warnings: Failure[];
  selectOptions?: FieldSelectOption<T>[];
  selectOptionsProviderAction?: string;
  onChange: (change: InputChanged<T>) => void;
}

function ProviderFieldFormGroup<T>({
  advancedSettings = false,
  name,
  label,
  helpText,
  helpTextWarning,
  helpLink,
  placeholder,
  value,
  type: providerType,
  advanced,
  hidden,
  pending,
  errors,
  warnings,
  selectOptions,
  selectOptionsProviderAction,
  provider,
  onChange,
  ...otherProps
}: ProviderFieldFormGroupProps<T>) {
  const type = useMemo(() => {
    switch (providerType) {
      case 'captcha':
        return 'captcha';
      case 'checkbox':
        return 'check';
      case 'device':
        return 'device';
      case 'keyValueList':
        return 'keyValueList';
      case 'password':
        return 'password';
      case 'number':
        return 'number';
      case 'path':
        return 'path';
      case 'filePath':
        return 'path';
      case 'select':
        if (selectOptionsProviderAction) {
          return 'dynamicSelect';
        }
        return 'select';
      case 'seriesTag':
        return 'seriesTag';
      case 'tag':
        return 'textTag';
      case 'tagSelect':
        return 'tagSelect';
      case 'textbox':
        return 'text';
      case 'oAuth':
        return 'oauth';
      case 'rootFolder':
        return 'rootFolderSelect';
      case 'qualityProfile':
        return 'qualityProfileSelect';
      default:
        return 'text';
    }
  }, [providerType, selectOptionsProviderAction]);

  const selectValues = useMemo(() => {
    if (!selectOptions) {
      return;
    }

    return selectOptions.map((option) => {
      return {
        key: option.value,
        value: option.name,
        hint: option.hint,
      };
    });
  }, [selectOptions]);

  // GH #180 scope C: derive a per-field testid for Playwright fixtures.
  // Shape `settings-{provider}-field-{name}` matches the existing
  // `settings-{provider}-field-*` convention used in Settings PageObjects
  // (e.g. settings-indexer-field-name). Only derived when `provider` is
  // supplied by the parent EditModalContent — Add modal flows that omit
  // provider keep the testid unset (no contract drift). The propagation
  // lands on the wrapping <label> for CHECK inputs (per scope A) and on
  // the <input> for TEXT/NUMBER/PASSWORD/etc. (per TextInput.tsx:185).
  const fieldTestId = provider ? `settings-${provider}-field-${name}` : undefined;

  if (hidden === 'hidden' || (hidden === 'hiddenIfNotSet' && !value)) {
    return null;
  }

  return (
    <FormGroup advancedSettings={advancedSettings} isAdvanced={advanced}>
      <FormLabel>{label}</FormLabel>

      <FormInputGroup
        type={type}
        name={name}
        helpText={helpText}
        helpTextWarning={helpTextWarning}
        helpLink={helpLink}
        placeholder={placeholder}
        // @ts-expect-error - this isn't available on all types
        selectOptionsProviderAction={selectOptionsProviderAction}
        value={value}
        values={selectValues}
        errors={errors}
        warnings={warnings}
        pending={pending}
        includeFiles={providerType === 'filePath' ? true : undefined}
        data-testid={fieldTestId}
        onChange={onChange}
        {...otherProps}
      />
    </FormGroup>
  );
}

export default ProviderFieldFormGroup;
