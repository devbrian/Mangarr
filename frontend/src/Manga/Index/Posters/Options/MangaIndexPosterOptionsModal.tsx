import React from 'react';
import Modal from 'Components/Modal/Modal';
import MangaIndexPosterOptionsModalContent from './MangaIndexPosterOptionsModalContent';

interface MangaIndexPosterOptionsModalProps {
  isOpen: boolean;
  onModalClose(...args: unknown[]): unknown;
}

function MangaIndexPosterOptionsModal({
  isOpen,
  onModalClose,
}: MangaIndexPosterOptionsModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <MangaIndexPosterOptionsModalContent onModalClose={onModalClose} />
    </Modal>
  );
}

export default MangaIndexPosterOptionsModal;
