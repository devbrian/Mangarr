import React, { useCallback, useEffect } from 'react';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { useManageImportListExclusion } from '../useImportListExclusions';

// Phase 26 Plan 26-05 (IL-05) — combined Add/Edit modal for an
// ImportListExclusion row. Mirrors Sonarr-ref's `EditImportListExclusionModal`
// shape but inlines the modal content (single file per Plan 26-05
// files_modified budget). Manga-shape triplet replaces Sonarr's single TvdbId
// per Migration 003 — `MangaDexId` is the canonical UNIQUE-indexed identifier
// (string GUID); `MalId` + `AniListId` are optional secondary IDs for
// AniList-only / MAL-only exclusions per RESEARCH §Q4 NULL-tolerance.

interface EditImportListExclusionModalProps {
  id?: number;
  title?: string;
  mangaDexId?: string;
  malId?: number;
  aniListId?: number;
  isOpen: boolean;
  onModalClose: () => void;
  onDeleteImportListExclusionPress?: () => void;
}

function EditImportListExclusionModal({
  id,
  title: existingTitle,
  mangaDexId: existingMangaDexId,
  malId: existingMalId,
  aniListId: existingAniListId,
  isOpen,
  onModalClose,
  onDeleteImportListExclusionPress,
}: EditImportListExclusionModalProps) {
  const {
    item,
    isSaving,
    saveError,
    validationErrors,
    validationWarnings,
    updateValue,
    save,
  } = useManageImportListExclusion({
    id,
    title: existingTitle,
    mangaDexId: existingMangaDexId,
    malId: existingMalId,
    aniListId: existingAniListId,
  });

  const { title, mangaDexId, malId, aniListId } = item;
  const wasSaving = usePrevious(isSaving);

  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, wasSaving, saveError, onModalClose]);

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      updateValue(name, value);
    },
    [updateValue]
  );

  const handleSavePress = useCallback(() => {
    save();
  }, [save]);

  return (
    <Modal size={sizes.MEDIUM} isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent
        data-testid="edit-importlist-exclusion-modal"
        onModalClose={onModalClose}
      >
        <ModalHeader>
          {id
            ? translate('EditImportListExclusion')
            : translate('AddImportListExclusion')}
        </ModalHeader>

        <ModalBody>
          <Form
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            <FormGroup>
              <FormLabel>{translate('Title')}</FormLabel>

              <FormInputGroup
                type={inputTypes.TEXT}
                name="title"
                helpText={translate('MangaTitleToExcludeHelpText')}
                {...title}
                onChange={handleInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>{translate('MangaDexId')}</FormLabel>

              <FormInputGroup
                type={inputTypes.TEXT}
                name="mangaDexId"
                helpText={translate('MangaDexIdExcludeHelpText')}
                {...mangaDexId}
                onChange={handleInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>{translate('MalId')}</FormLabel>

              <FormInputGroup
                type={inputTypes.NUMBER}
                name="malId"
                helpText={translate('MalIdExcludeHelpText')}
                {...malId}
                onChange={handleInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>{translate('AniListId')}</FormLabel>

              <FormInputGroup
                type={inputTypes.NUMBER}
                name="aniListId"
                helpText={translate('AniListIdExcludeHelpText')}
                {...aniListId}
                onChange={handleInputChange}
              />
            </FormGroup>
          </Form>
        </ModalBody>

        <ModalFooter>
          {/* CodeRabbit PR #218: only render Delete when both the id AND the
              handler exist — the handler is optional on the props interface.
              Without the handler guard, the button would `onPress={undefined}`
              and become a silent click target on Edit-mode invocations that
              didn't wire a delete callback (e.g., the parent row passes
              onDeleteImportListExclusionPress conditionally). */}
          {id && onDeleteImportListExclusionPress ? (
            <Button
              kind={kinds.DANGER}
              onPress={onDeleteImportListExclusionPress}
            >
              {translate('Delete')}
            </Button>
          ) : null}

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
    </Modal>
  );
}

export default EditImportListExclusionModal;
