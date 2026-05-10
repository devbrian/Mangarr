import React, { useCallback, useEffect } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { inputTypes, kinds } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import {
  useManageMetadataSource,
  useSetPrimaryMetadataSource,
} from 'Settings/MetadataSource/useMetadataSources';
import { SelectedSchema } from 'Settings/useProviderSchema';
import { EnhancedSelectInputChanged, InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './EditMetadataSourceModalContent.css';

export interface EditMetadataSourceModalContentProps {
  id?: number;
  selectedSchema?: SelectedSchema;
  onModalClose: () => void;
  onDeleteMetadataSourcePress?: () => void;
}

function EditMetadataSourceModalContent({
  id,
  selectedSchema,
  onModalClose,
  onDeleteMetadataSourcePress,
}: EditMetadataSourceModalContentProps) {
  const showAdvancedSettings = useShowAdvancedSettings();

  const {
    item,
    updateFieldValue,
    updateValue,
    saveProvider,
    isSaving,
    saveError,
    testProvider,
    isTesting,
    validationErrors,
    validationWarnings,
  } = useManageMetadataSource(id, selectedSchema);

  const wasSaving = usePrevious(isSaving);

  // D-15 SetPrimary is a server-enforced atomic operation — it bypasses the standard
  // pending-changes/save flow. The button is shown only for already-persisted rows that
  // are NOT currently primary (you can't "promote" a row that doesn't exist yet and you
  // can't "promote" the already-primary row).
  const { setPrimary, isSettingPrimary, setPrimaryError } =
    useSetPrimaryMetadataSource(id ?? 0, () => {
      onModalClose();
    });

  const { implementationName, name, fields, tags, message, isPrimary } = item;

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      // @ts-expect-error - change is not yet typed
      updateValue(change.name, change.value);
    },
    [updateValue]
  );

  const handleFieldChange = useCallback(
    ({
      name,
      value,
      additionalProperties,
    }: EnhancedSelectInputChanged<unknown>) => {
      updateFieldValue({ [name]: value, ...additionalProperties });
    },
    [updateFieldValue]
  );

  const handleTestPress = useCallback(() => {
    testProvider();
  }, [testProvider]);

  const handleSavePress = useCallback(() => {
    saveProvider();
  }, [saveProvider]);

  const handleSetPrimaryPress = useCallback(() => {
    setPrimary();
  }, [setPrimary]);

  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, wasSaving, saveError, onModalClose]);

  // Phase 2 backend D-15: the modal surfaces `Set as Primary` only for persisted rows
  // that aren't already primary. New rows can't be promoted (no id), already-primary
  // rows have nothing to do. NOTE: isPrimary is intentionally NOT exposed as an editable
  // checkbox here — the at-most-one invariant requires going through the dedicated
  // SetPrimary mutation (atomic demote-all-then-promote-one server-side).
  const isPrimaryValue = isPrimary?.value ?? false;
  const canSetPrimary = !!id && !isPrimaryValue;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {id
          ? translate('EditMetadataSourceImplementation', {
              implementationName: implementationName?.value ?? '',
            })
          : translate('AddMetadataSourceImplementation', {
              implementationName: implementationName?.value ?? '',
            })}
      </ModalHeader>

      <ModalBody>
        <Form
          validationErrors={validationErrors}
          validationWarnings={validationWarnings}
        >
          {message ? (
            <Alert className={styles.message} kind={message.value.type}>
              {message.value.message}
            </Alert>
          ) : null}

          {setPrimaryError ? (
            <Alert className={styles.message} kind={kinds.DANGER}>
              {translate('SetPrimaryMetadataSourceError')}
            </Alert>
          ) : null}

          <FormGroup>
            <FormLabel>{translate('Name')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TEXT}
              name="name"
              {...name}
              onChange={handleInputChange}
            />
          </FormGroup>

          {id ? (
            <FormGroup>
              <FormLabel>{translate('Primary')}</FormLabel>

              <div>
                <Label kind={isPrimaryValue ? kinds.SUCCESS : kinds.DISABLED}>
                  {isPrimaryValue
                    ? translate('Primary')
                    : translate('Secondary')}
                </Label>

                <FormInputHelpText
                  text={translate('MetadataSourcePrimaryHelpText')}
                />
              </div>
            </FormGroup>
          ) : null}

          {fields?.map((field) => {
            return (
              <ProviderFieldFormGroup
                key={field.name}
                {...field}
                advancedSettings={showAdvancedSettings}
                provider="metadataSource"
                providerData={item}
                onChange={handleFieldChange}
              />
            );
          })}

          <FormGroup>
            <FormLabel>{translate('Tags')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TAG}
              name="tags"
              helpText={translate('MetadataSourceTagsHelpText')}
              {...tags}
              onChange={handleInputChange}
            />
          </FormGroup>
        </Form>
      </ModalBody>

      <ModalFooter>
        {id ? (
          <Button
            className={styles.deleteButton}
            kind={kinds.DANGER}
            onPress={onDeleteMetadataSourcePress}
          >
            {translate('Delete')}
          </Button>
        ) : null}

        {canSetPrimary ? (
          <SpinnerErrorButton
            kind={kinds.PRIMARY}
            isSpinning={isSettingPrimary}
            error={setPrimaryError}
            onPress={handleSetPrimaryPress}
          >
            {translate('SetAsPrimary')}
          </SpinnerErrorButton>
        ) : null}

        <AdvancedSettingsButton showLabel={false} />

        <SpinnerErrorButton
          isSpinning={isTesting}
          error={saveError}
          onPress={handleTestPress}
        >
          {translate('Test')}
        </SpinnerErrorButton>

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          onPress={handleSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditMetadataSourceModalContent;
