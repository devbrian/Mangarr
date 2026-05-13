// Sonarr divergence: NEW manga peer of upstream
// frontend/src/Series/MoveSeries/MoveSeriesModal.tsx per issue #81 — see
// DIVERGENCE.md. The Sonarr peer was deleted as a no-op stub in Phase 17.3
// Plan 17.3-03 D-11 before a real port could land; this file re-authors it
// 1:1 with the Series -> Manga rename.
//
// Confirmation dialog shown after the user changes the manga root folder
// in either the single-edit modal (Manga/Edit/EditMangaModalContent.tsx)
// or the bulk-edit modal (Manga/Index/Select/Edit/EditMangaModalContent.tsx).
// Cancel closes the modal without saving; "No, I'll Move the Files Manually"
// saves with moveFiles=false (DB path-update only); "Yes, Move the Files"
// saves with moveFiles=true (backend enqueues MoveMangaCommand /
// BulkMoveMangaCommand which routes through MoveMangaService.MoveSingleManga
// + IDiskTransferService.TransferFolder(TransferMode.Move) with cross-drive
// copy+verify+delete fallback).
import React from 'react';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './MoveMangaModal.css';

interface MoveMangaModalProps {
  originalPath?: string;
  destinationPath?: string;
  destinationRootFolder?: string;
  isOpen: boolean;
  onModalClose: () => void;
  onSavePress: () => void;
  onMoveMangaPress: () => void;
}

function MoveMangaModal({
  originalPath,
  destinationPath,
  destinationRootFolder,
  isOpen,
  onModalClose,
  onSavePress,
  onMoveMangaPress,
}: MoveMangaModalProps) {
  if (isOpen && !originalPath && !destinationPath && !destinationRootFolder) {
    console.error(
      'originalPath and destinationPath OR destinationRootFolder must be provided'
    );
  }

  return (
    <Modal
      isOpen={isOpen}
      size={sizes.MEDIUM}
      closeOnBackgroundClick={false}
      onModalClose={onModalClose}
    >
      <ModalContent showCloseButton={true} onModalClose={onModalClose}>
        <ModalHeader>{translate('MoveFiles')}</ModalHeader>

        <ModalBody>
          {destinationRootFolder
            ? translate('MoveMangaFoldersToRootFolder', {
                destinationRootFolder,
              })
            : null}

          {originalPath && destinationPath
            ? translate('MoveMangaFoldersToNewPath', {
                originalPath,
                destinationPath,
              })
            : null}
        </ModalBody>

        <ModalFooter>
          <Button className={styles.doNotMoveButton} onPress={onSavePress}>
            {translate('MoveMangaFoldersDontMoveFiles')}
          </Button>

          <Button kind={kinds.DANGER} onPress={onMoveMangaPress}>
            {translate('MoveMangaFoldersMoveFiles')}
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default MoveMangaModal;
