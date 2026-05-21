// Phase 27.1 Plan 27.1-03 (IL-EXCLUSIONS) — split-out form body of the
// EditImportListExclusionModal. Verbatim hoist of the Phase 26 substrate's
// combined Modal+Content file body (`<ModalContent>` and its children) into
// its own file, matching Sonarr v5-develop's verbatim shell+content split.
// The form fields are already manga-adapted to the 3-ID shape (Title,
// MangaDexId, MalId, AniListId via useManageImportListExclusion); only the
// surrounding `<Modal>` wrapper moves to the parent shell.
import React, { useCallback, useEffect } from 'react';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { inputTypes, kinds } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { useManageImportListExclusion } from '../useImportListExclusions';
import styles from './EditImportListExclusionModalContent.css';

interface EditImportListExclusionModalContentProps {
  id?: number;
  title?: string;
  mangaDexId?: string;
  malId?: number;
  aniListId?: number;
  onModalClose: () => void;
  onDeleteImportListExclusionPress?: () => void;
}

function EditImportListExclusionModalContent({
  id,
  title: existingTitle,
  mangaDexId: existingMangaDexId,
  malId: existingMalId,
  aniListId: existingAniListId,
  onModalClose,
  onDeleteImportListExclusionPress,
}: EditImportListExclusionModalContentProps) {
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
            didn't wire a delete callback. */}
        {id && onDeleteImportListExclusionPress ? (
          <Button
            className={styles.deleteButton}
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
  );
}

export default EditImportListExclusionModalContent;
