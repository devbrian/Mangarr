import React from 'react';
import Modal from 'Components/Modal/Modal';
import OrganizeMangaModalContent, {
  OrganizeMangaModalContentProps,
} from './OrganizeMangaModalContent';

interface OrganizeMangaModalProps extends OrganizeMangaModalContentProps {
  isOpen: boolean;
}

function OrganizeMangaModal(props: OrganizeMangaModalProps) {
  const { isOpen, onModalClose, ...otherProps } = props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <OrganizeMangaModalContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default OrganizeMangaModal;
