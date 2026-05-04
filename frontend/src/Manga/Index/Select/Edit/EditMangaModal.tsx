import React from 'react';
import Modal from 'Components/Modal/Modal';
import EditMangaModalContent, {
  EditMangaModalContentProps,
} from './EditMangaModalContent';

interface EditMangaModalProps extends EditMangaModalContentProps {
  isOpen: boolean;
}

function EditMangaModal({
  isOpen,
  onSavePress,
  onModalClose,
}: EditMangaModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <EditMangaModalContent
        onSavePress={onSavePress}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default EditMangaModal;
