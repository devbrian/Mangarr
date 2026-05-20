import React from 'react';
import Modal from 'Components/Modal/Modal';
import AddImportListModalContent, {
  AddImportListModalContentProps,
} from './AddImportListModalContent';

interface AddImportListModalProps extends AddImportListModalContentProps {
  isOpen: boolean;
}

function AddImportListModal({
  isOpen,
  onImportListSelect,
  onModalClose,
}: AddImportListModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <AddImportListModalContent
        onImportListSelect={onImportListSelect}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default AddImportListModal;
