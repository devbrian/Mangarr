import React, { useCallback, useEffect } from 'react';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
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
import { SelectedSchema } from 'Settings/useProviderSchema';
import { EnhancedSelectInputChanged, InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { useManageImportList } from '../useImportLists';
import styles from './EditImportListModalContent.css';

// Phase 26 Plan 26-05 (IL-05) — Edit modal content. Mirror of
// frontend/src/Settings/Indexers/Indexers/EditIndexerModalContent.tsx per
// RESEARCH §Q7. Field surface drawn from `ImportListResource`. Manga peers
// of the Sonarr substitution map (Phase 5 D-04 + Phase 17.3 D-13 rename pass):
//   * translationProfileId  (manga peer of the deleted TV quality profile id)
//   * searchForMissingChapters  (manga peer of the deleted SearchForMissing-
//     Episodes Sonarr field; Phase 6 rename)
//   * Season / SeriesType TV-only fields DROPPED (Migration 001)
//   * customFormatProfileId — wired via CUSTOM_FORMAT_PROFILE_SELECT
//     (quick-260608-vf9 follow-up; the FE input deferred at Phase 26 now ships,
//     defaulting to the isDefault custom format profile).
//   * Monitor — preserved (MONITOR_EPISODES_SELECT); new lists default to All.
//   * MonitorNewItems — REMOVED from the form (quick-260608-vf9 follow-up): it
//     was Sonarr's "monitor new seasons" control rendered against an empty
//     manga options stub (no selectable values). The backing flag still drives
//     new-chapter auto-monitoring (ChapterSynthesisService) and is defaulted to
//     All for new lists in useImportLists.ts, so new chapters keep being
//     monitored without the confusing empty dropdown.

export interface EditImportListModalContentProps {
  id?: number;
  cloneId?: number;
  selectedSchema?: SelectedSchema;
  onModalClose: () => void;
  onDeleteImportListPress?: () => void;
}

function EditImportListModalContent({
  id,
  cloneId,
  selectedSchema,
  onModalClose,
  onDeleteImportListPress,
}: EditImportListModalContentProps) {
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
  } = useManageImportList(id, cloneId, selectedSchema);

  const wasSaving = usePrevious(isSaving);

  const {
    implementationName = '',
    name,
    enableAutomaticAdd,
    searchForMissingChapters,
    shouldMonitor,
    rootFolderPath,
    translationProfileId,
    customFormatProfileId,
    tags,
    fields,
  } = item;

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      // @ts-expect-error - InputChanged is not typed correctly
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

  const handleSavePress = useCallback(() => {
    saveProvider();
  }, [saveProvider]);

  const handleTestPress = useCallback(() => {
    testProvider();
  }, [testProvider]);

  useEffect(() => {
    if (!isSaving && wasSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, wasSaving, saveError, onModalClose]);

  return (
    <ModalContent
      data-testid="edit-importlist-modal"
      onModalClose={onModalClose}
    >
      <ModalHeader>
        {id
          ? translate('EditImportListImplementation', { implementationName })
          : translate('AddImportListImplementation', { implementationName })}
      </ModalHeader>

      <ModalBody>
        <Form
          validationErrors={validationErrors}
          validationWarnings={validationWarnings}
        >
          <FormGroup>
            <FormLabel>{translate('Name')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TEXT}
              name="name"
              data-testid="settings-importlist-field-name"
              {...name}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('EnableAutomaticAdd')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="enableAutomaticAdd"
              helpText={translate('EnableAutomaticAddImportListHelpText')}
              {...enableAutomaticAdd}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('SearchForMissingChapters')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="searchForMissingChapters"
              helpText={translate('SearchForMissingChaptersHelpText')}
              {...searchForMissingChapters}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('Monitor')}</FormLabel>

            <FormInputGroup
              type={inputTypes.MONITOR_EPISODES_SELECT}
              name="shouldMonitor"
              onChange={handleInputChange}
              {...shouldMonitor}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('RootFolder')}</FormLabel>

            <FormInputGroup
              type={inputTypes.ROOT_FOLDER_SELECT}
              name="rootFolderPath"
              helpText={translate('ListRootFolderHelpText')}
              {...rootFolderPath}
              includeMissingValue={true}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('TranslationProfile')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TRANSLATION_PROFILE_SELECT}
              name="translationProfileId"
              helpText={translate('ListTranslationProfileHelpText')}
              {...translationProfileId}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('CustomFormatProfile')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CUSTOM_FORMAT_PROFILE_SELECT}
              name="customFormatProfileId"
              helpText={translate('ListCustomFormatProfileHelpText')}
              {...customFormatProfileId}
              onChange={handleInputChange}
            />
          </FormGroup>

          {fields?.map((field) => {
            return (
              <ProviderFieldFormGroup
                key={field.name}
                advancedSettings={showAdvancedSettings}
                provider="importlist"
                providerData={item}
                {...field}
                onChange={handleFieldChange}
              />
            );
          })}

          <FormGroup>
            <FormLabel>{translate('Tags')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TAG}
              name="tags"
              helpText={translate('ListTagsHelpText')}
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
            data-testid="delete-button"
            onPress={onDeleteImportListPress}
          >
            {translate('Delete')}
          </Button>
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
          data-testid="save-button"
          onPress={handleSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditImportListModalContent;
