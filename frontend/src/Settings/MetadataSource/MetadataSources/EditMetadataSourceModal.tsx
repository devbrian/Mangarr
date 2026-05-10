import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditMetadataSourceModalContent, {
  EditMetadataSourceModalContentProps,
} from './EditMetadataSourceModalContent';

interface EditMetadataSourceModalProps
  extends EditMetadataSourceModalContentProps {
  isOpen: boolean;
}

function EditMetadataSourceModal({
  isOpen,
  onModalClose,
  ...otherProps
}: EditMetadataSourceModalProps) {
  return (
    <Modal size={sizes.MEDIUM} isOpen={isOpen} onModalClose={onModalClose}>
      <EditMetadataSourceModalContent
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default EditMetadataSourceModal;
