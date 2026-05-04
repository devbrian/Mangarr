import React from 'react';
import Modal from 'Components/Modal/Modal';
import MangaIndexOverviewOptionsModalContent from './MangaIndexOverviewOptionsModalContent';

interface MangaIndexOverviewOptionsModalProps {
  isOpen: boolean;
  onModalClose(...args: unknown[]): void;
}

function MangaIndexOverviewOptionsModal({
  isOpen,
  onModalClose,
  ...otherProps
}: MangaIndexOverviewOptionsModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <MangaIndexOverviewOptionsModalContent
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default MangaIndexOverviewOptionsModal;
