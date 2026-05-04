import React from 'react';
import Modal from 'Components/Modal/Modal';
import DeleteMangaModalContent, {
  DeleteMangaFilesModalContentProps,
} from './DeleteMangaFilesModalContent';

interface DeleteMangaFilesModalProps
  extends DeleteMangaFilesModalContentProps {
  isOpen: boolean;
}

function DeleteMangaFilesModal(props: DeleteMangaFilesModalProps) {
  const { isOpen, onModalClose } = props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <DeleteMangaModalContent onModalClose={onModalClose} />
    </Modal>
  );
}

export default DeleteMangaFilesModal;
