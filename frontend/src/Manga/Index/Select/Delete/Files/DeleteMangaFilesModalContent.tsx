import React, { useCallback } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import MangaDeleteList from '../MangaDeleteList';
import useSelectedMangaStats from '../useSelectedMangaStats';
import styles from './DeleteMangaFilesModalContent.css';

export interface DeleteMangaFilesModalContentProps {
  onModalClose(): void;
}

function DeleteMangaFilesModalContent({
  onModalClose,
}: DeleteMangaFilesModalContentProps) {
  const { manga, mangaIds, totalEpisodeFileCount, totalSizeOnDisk } =
    useSelectedMangaStats();
  const executeCommand = useExecuteCommand();

  const onDeleteMangaConfirmed = useCallback(() => {
    executeCommand({
      name: CommandNames.DeleteSeriesFiles,
      mangaIds,
    });

    onModalClose();
  }, [mangaIds, executeCommand, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('DeleteSelectedSeriesFiles')}</ModalHeader>

      <ModalBody>
        <div className={styles.message}>
          {translate('DeleteMangaFilesConfirmation', {
            count: manga.length,
          })}
        </div>

        <MangaDeleteList
          manga={manga}
          showFileDetails={true}
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

export default DeleteMangaFilesModalContent;
