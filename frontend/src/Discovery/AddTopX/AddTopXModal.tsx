// Phase 42 Plan 42-07 — count-only bulk-add modal wrapper (sketch 002, LOCKED).
// Role-match analog: AddManga/AddNewManga/AddNewMangaModal.tsx (thin Modal wrapper).
import React from 'react';
import Modal from 'Components/Modal/Modal';
import AddTopXModalContent, {
  AddTopXModalContentProps,
} from './AddTopXModalContent';

interface AddTopXModalProps extends AddTopXModalContentProps {
  isOpen: boolean;
}

function AddTopXModal({
  isOpen,
  onModalClose,
  ...otherProps
}: AddTopXModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <AddTopXModalContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default AddTopXModal;
