import React, { useCallback, useEffect, useRef, useState } from 'react';
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
  useMetadataSourcesData,
  useManageMetadataSource,
  useSetPrimaryMetadataSource,
} from 'Settings/MetadataSource/useMetadataSources';
import { SelectedSchema } from 'Settings/useProviderSchema';
import {
  CheckInputChanged,
  EnhancedSelectInputChanged,
  InputChanged,
} from 'typings/inputs';
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

  // "Set as Primary on Save" checkbox state — local React state, NOT a form
  // pending-change. The actual promotion is enforced server-side by a separate
  // atomic mutation (POST /metadatasource/{id}/setprimary) that demotes all
  // other primaries; we never PUT `isPrimary: true` directly. Surfacing the
  // intent in the form just lets users add+promote in a single click instead
  // of save -> reopen -> Set as Primary.
  const [setAsPrimaryOnSave, setSetAsPrimaryOnSave] = useState(false);

  // Used to look up the just-created row's id by name after a new-source save
  // (the modal's own `id` prop stays undefined; the new id only exists in the
  // refreshed metadata-source cache).
  const allSources = useMetadataSourcesData();

  // Snapshot of the name the user typed at save-click time. Captured here
  // because `useManageProviderSettings.handleSaveSuccess` calls
  // `clearPendingChanges()` after a successful save, which reverts the form's
  // `name.value` back to the schema's `implementationName` default. Without
  // the snapshot, the post-save name-lookup against `allSources` would match
  // the wrong row (the schema's default name e.g. "MangaDex" instead of the
  // user-typed "MangaDex Backup").
  const promoteNameRef = useRef<string | undefined>(undefined);

  // D-15 SetPrimary is a server-enforced atomic operation — bypasses the standard
  // pending-changes/save flow. `setPrimary` accepts an optional `idOverride`
  // argument so the new-source flow can promote a row whose id was unknown at
  // hook-creation time (looked up by name from the cache below).
  const { setPrimary, isSettingPrimary, setPrimaryError } =
    useSetPrimaryMetadataSource(id ?? 0, () => {
      onModalClose();
    });

  const wasSettingPrimary = usePrevious(isSettingPrimary);

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
    // Snapshot the user-typed name before save — pendingChanges is cleared on
    // save success and the form reverts to the schema default, breaking the
    // post-save name lookup.
    if (setAsPrimaryOnSave && !id) {
      promoteNameRef.current = name?.value;
    }
    saveProvider();
  }, [saveProvider, setAsPrimaryOnSave, id, name?.value]);

  const handleSetPrimaryPress = useCallback(() => {
    setPrimary();
  }, [setPrimary]);

  const handleSetAsPrimaryOnSaveChange = useCallback(
    ({ value }: CheckInputChanged) => {
      setSetAsPrimaryOnSave(value);
    },
    []
  );

  // Phase 2 backend D-15: NOTE that isPrimary is still NOT exposed as an editable
  // form field on the entity itself — the at-most-one invariant requires the
  // dedicated SetPrimary mutation (atomic demote-all-then-promote-one server-side).
  // The checkbox below is a *local* "promote after save" intent flag, not a form
  // field; the modal save flow chains saveProvider() → setPrimary(newId) when set.
  const isPrimaryValue = isPrimary?.value ?? false;
  const canSetPrimary = !!id && !isPrimaryValue;

  // Save-completion handler: if the user checked "Set as Primary", chain the
  // SetPrimary mutation against the row's id (existing source: bound id; new
  // source: looked up by name from the refreshed cache). Otherwise close the
  // modal as before.
  useEffect(() => {
    if (!wasSaving || isSaving || saveError) {
      return;
    }

    if (!setAsPrimaryOnSave || isPrimaryValue) {
      onModalClose();
      return;
    }

    if (id) {
      // Existing source — promote with bound id.
      setPrimary();
      return;
    }

    // New source — look up the just-created row by name in the refreshed cache.
    // Use the snapshot captured at save-click time (post-save the form's
    // `name?.value` reverts to the schema default after pendingChanges clears).
    const targetName = promoteNameRef.current;
    const newRow = targetName
      ? allSources.find((s) => s.name === targetName)
      : undefined;

    if (newRow) {
      promoteNameRef.current = undefined;
      setPrimary(newRow.id);
    } else {
      // Fallback: if we can't find the new row (race / rename), close anyway.
      promoteNameRef.current = undefined;
      onModalClose();
    }
  }, [
    wasSaving,
    isSaving,
    saveError,
    setAsPrimaryOnSave,
    isPrimaryValue,
    id,
    allSources,
    setPrimary,
    onModalClose,
  ]);

  // SetPrimary completion handler: close modal once the chained promotion lands.
  useEffect(() => {
    if (
      wasSettingPrimary &&
      !isSettingPrimary &&
      !setPrimaryError
    ) {
      onModalClose();
    }
  }, [wasSettingPrimary, isSettingPrimary, setPrimaryError, onModalClose]);

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

          <FormGroup>
            <FormLabel>{translate('Primary')}</FormLabel>

            {isPrimaryValue ? (
              // Already primary — read-only status. Demotion isn't a supported
              // D-15 operation (you "set primary on another source" instead).
              <div>
                <Label kind={kinds.SUCCESS}>{translate('Primary')}</Label>
                <FormInputHelpText
                  text={translate('MetadataSourcePrimaryHelpText')}
                />
              </div>
            ) : (
              <FormInputGroup
                type={inputTypes.CHECK}
                name="setAsPrimaryOnSave"
                value={setAsPrimaryOnSave}
                helpText={translate('SetAsPrimaryOnSaveHelpText')}
                onChange={handleSetAsPrimaryOnSaveChange}
              />
            )}
          </FormGroup>

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
