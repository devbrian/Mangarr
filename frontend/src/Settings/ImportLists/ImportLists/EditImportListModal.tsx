import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditImportListModalContent, {
  EditImportListModalContentProps,
} from './EditImportListModalContent';

interface EditImportListModalProps extends EditImportListModalContentProps {
  isOpen: boolean;
}

function EditImportListModal({
  isOpen,
  onModalClose,
  ...otherProps
}: EditImportListModalProps) {
  return (
    <Modal size={sizes.MEDIUM} isOpen={isOpen} onModalClose={onModalClose}>
      <EditImportListModalContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default EditImportListModal;
