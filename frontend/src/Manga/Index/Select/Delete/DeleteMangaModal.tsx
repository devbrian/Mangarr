import React from 'react';
import Modal from 'Components/Modal/Modal';
import DeleteMangaModalContent, {
  DeleteMangaModalContentProps,
} from './DeleteMangaModalContent';

interface DeleteMangaModalProps extends DeleteMangaModalContentProps {
  isOpen: boolean;
}

function DeleteMangaModal(props: DeleteMangaModalProps) {
  const { isOpen, onModalClose } = props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <DeleteMangaModalContent onModalClose={onModalClose} />
    </Modal>
  );
}

export default DeleteMangaModal;
