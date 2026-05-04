import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import {
  setMangaDeleteOptions,
  useMangaDeleteOptions,
} from 'Manga/mangaOptionsStore';
import { useBulkDeleteManga } from 'Manga/useManga';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import MangaDeleteList from './MangaDeleteList';
import useSelectedMangaStats from './useSelectedMangaStats';
import styles from './DeleteMangaModalContent.css';

export interface DeleteMangaModalContentProps {
  onModalClose(): void;
}

function DeleteMangaModalContent({
  onModalClose,
}: DeleteMangaModalContentProps) {
  const { addImportListExclusion } = useMangaDeleteOptions();
  const { bulkDeleteManga } = useBulkDeleteManga();
  const [deleteFiles, setDeleteFiles] = useState(false);
  const { manga, mangaIds, totalEpisodeFileCount, totalSizeOnDisk } =
    useSelectedMangaStats();

  const onDeleteFilesChange = useCallback(
    ({ value }: InputChanged<boolean>) => {
      setDeleteFiles(value);
    },
    [setDeleteFiles]
  );

  const onDeleteOptionChange = useCallback(
    ({ name, value }: { name: string; value: boolean }) => {
      setMangaDeleteOptions({
        [name]: value,
      });
    },
    []
  );

  const onDeleteMangaConfirmed = useCallback(() => {
    setDeleteFiles(false);

    bulkDeleteManga({
      mangaIds,
      deleteFiles,
      addImportListExclusion,
    });

    onModalClose();
  }, [
    deleteFiles,
    addImportListExclusion,
    setDeleteFiles,
    mangaIds,
    bulkDeleteManga,
    onModalClose,
  ]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('DeleteSelectedSeries')}</ModalHeader>

      <ModalBody>
        <div>
          <FormGroup>
            <FormLabel>{translate('AddListExclusion')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="addImportListExclusion"
              value={addImportListExclusion}
              helpText={translate('AddListExclusionSeriesHelpText')}
              onChange={onDeleteOptionChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>
              {manga.length > 1
                ? translate('DeleteMangaFolders')
                : translate('DeleteMangaFolder')}
            </FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="deleteFiles"
              value={deleteFiles}
              helpText={
                manga.length > 1
                  ? translate('DeleteMangaFoldersHelpText')
                  : translate('DeleteMangaFolderHelpText')
              }
              kind="danger"
              onChange={onDeleteFilesChange}
            />
          </FormGroup>
        </div>

        <div className={styles.message}>
          {deleteFiles
            ? translate('DeleteMangaFolderCountWithFilesConfirmation', {
                count: manga.length,
              })
            : translate('DeleteMangaFolderCountConfirmation', {
                count: manga.length,
              })}
        </div>

        <MangaDeleteList
          manga={manga}
          showFileDetails={deleteFiles}
          totalEpisodeFileCount={totalEpisodeFileCount}
          totalSizeOnDisk={totalSizeOnDisk}
          styles={styles}
        />
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <Button kind={kinds.DANGER} onPress={onDeleteMangaConfirmed}>
          {translate('Delete')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default DeleteMangaModalContent;
