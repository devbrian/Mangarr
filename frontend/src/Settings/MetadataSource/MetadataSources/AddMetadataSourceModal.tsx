import React from 'react';
import Modal from 'Components/Modal/Modal';
import AddMetadataSourceModalContent, {
  AddMetadataSourceModalContentProps,
} from './AddMetadataSourceModalContent';

interface AddMetadataSourceModalProps
  extends AddMetadataSourceModalContentProps {
  isOpen: boolean;
}

function AddMetadataSourceModal({
  isOpen,
  onModalClose,
  ...otherProps
}: AddMetadataSourceModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <AddMetadataSourceModalContent
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default AddMetadataSourceModal;
