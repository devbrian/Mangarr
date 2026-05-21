// Phase 27.1 Plan 27.1-04 Task 4 — Bulk-edit modal content. Port of the
// Sonarr v5-develop ManageImportListsEditModalContent.tsx with the manga-shape
// substitutions per PATTERNS:
//   * enableAutomaticAdd / rootFolderPath preserved verbatim (manga uses same names)
//   * Translation Profile field uses inputTypes.TRANSLATION_PROFILE_SELECT —
//     Mangarr canonical per Phase 5 D-04 manga model (matches the wiring
//     already shipped in EditImportListModalContent.tsx)
//   * searchForMissingChapters added per plan Task 4 spec (NO_CHANGE pattern;
//     parity with Sonarr's enableAutomaticSearch field on Indexers Edit modal)
//   * inputTypes.ROOT_FOLDER_SELECT — same constant as EditImportListModalContent's
//     rootFolderPath FormGroup (line 195) — preserved verbatim.
//
// Pattern kappa enforced: zero TV-shape tokens.
import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './ManageImportListsEditModalContent.css';

interface SavePayload {
  enableAutomaticAdd?: boolean;
  searchForMissingChapters?: boolean;
  rootFolderPath?: string;
  translationProfileId?: number;
}

interface ManageImportListsEditModalContentProps {
  importListIds: number[];
  onSavePress(payload: object): void;
  onModalClose(): void;
}

const NO_CHANGE = 'noChange';

const enableOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: NO_CHANGE,
    get value() {
      return translate('NoChange');
    },
    isDisabled: true,
  },
  {
    key: 'enabled',
    get value() {
      return translate('Enabled');
    },
  },
  {
    key: 'disabled',
    get value() {
      return translate('Disabled');
    },
  },
];

function ManageImportListsEditModalContent(
  props: ManageImportListsEditModalContentProps
) {
  const { importListIds, onSavePress, onModalClose } = props;

  const [enableAutomaticAdd, setEnableAutomaticAdd] = useState(NO_CHANGE);
  const [searchForMissingChapters, setSearchForMissingChapters] =
    useState(NO_CHANGE);
  const [rootFolderPath, setRootFolderPath] = useState(NO_CHANGE);
  const [translationProfileId, setTranslationProfileId] = useState<
    string | number
  >(NO_CHANGE);

  const save = useCallback(() => {
    let hasChanges = false;
    const payload: SavePayload = {};

    if (enableAutomaticAdd !== NO_CHANGE) {
      hasChanges = true;
      payload.enableAutomaticAdd = enableAutomaticAdd === 'enabled';
    }

    if (searchForMissingChapters !== NO_CHANGE) {
      hasChanges = true;
      payload.searchForMissingChapters = searchForMissingChapters === 'enabled';
    }

    if (rootFolderPath !== NO_CHANGE) {
      hasChanges = true;
      payload.rootFolderPath = rootFolderPath;
    }

    if (translationProfileId !== NO_CHANGE) {
      hasChanges = true;
      payload.translationProfileId = translationProfileId as number;
    }

    if (hasChanges) {
      onSavePress(payload);
    }

    onModalClose();
  }, [
    enableAutomaticAdd,
    searchForMissingChapters,
    rootFolderPath,
    translationProfileId,
    onSavePress,
    onModalClose,
  ]);

  const onInputChange = useCallback(({ name, value }: InputChanged) => {
    switch (name) {
      case 'enableAutomaticAdd':
        setEnableAutomaticAdd(value as string);
        break;
      case 'searchForMissingChapters':
        setSearchForMissingChapters(value as string);
        break;
      case 'rootFolderPath':
        setRootFolderPath(value as string);
        break;
      case 'translationProfileId':
        setTranslationProfileId(value as number);
        break;
      default:
        console.warn(`EditImportListsModalContent Unknown Input: '${name}'`);
    }
  }, []);

  const selectedCount = importListIds.length;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('EditSelectedImportLists')}</ModalHeader>

      <ModalBody>
        <FormGroup>
          <FormLabel>{translate('AutomaticAdd')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="enableAutomaticAdd"
            value={enableAutomaticAdd}
            values={enableOptions}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('SearchForMissingChapters')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="searchForMissingChapters"
            value={searchForMissingChapters}
            values={enableOptions}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('RootFolder')}</FormLabel>

          <FormInputGroup
            type={inputTypes.ROOT_FOLDER_SELECT}
            name="rootFolderPath"
            value={rootFolderPath}
            includeNoChange={true}
            includeNoChangeDisabled={false}
            selectedValueOptions={{ includeFreeSpace: false }}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('TranslationProfile')}</FormLabel>

          <FormInputGroup
            type={inputTypes.TRANSLATION_PROFILE_SELECT}
            name="translationProfileId"
            value={translationProfileId}
            includeNoChange={true}
            includeNoChangeDisabled={false}
            onChange={onInputChange}
          />
        </FormGroup>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div className={styles.selected}>
          {translate('CountImportListsSelected', {
            count: selectedCount,
          })}
        </div>

        <div>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <Button onPress={save}>{translate('ApplyChanges')}</Button>
        </div>
      </ModalFooter>
    </ModalContent>
  );
}

export default ManageImportListsEditModalContent;
