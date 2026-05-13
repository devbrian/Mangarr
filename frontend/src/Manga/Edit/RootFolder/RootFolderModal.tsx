// Sonarr divergence: NEW manga peer of upstream
// frontend/src/Series/Edit/RootFolder/RootFolderModal.tsx per issue #81 —
// see DIVERGENCE.md. 1:1 port with Series -> Manga rename.
//
// Destination picker shown when the user clicks the root-folder button on
// the single-manga Edit modal's Path field. Wraps RootFolderModalContent
// in the shared Modal shell.
import React from 'react';
import Modal from 'Components/Modal/Modal';
import RootFolderModalContent, {
  RootFolderModalContentProps,
} from './RootFolderModalContent';

interface RootFolderModalProps extends RootFolderModalContentProps {
  isOpen: boolean;
}

function RootFolderModal(props: RootFolderModalProps) {
  const { isOpen, rootFolderPath, mangaId, onSavePress, onModalClose } = props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <RootFolderModalContent
        mangaId={mangaId}
        rootFolderPath={rootFolderPath}
        onSavePress={onSavePress}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default RootFolderModal;
