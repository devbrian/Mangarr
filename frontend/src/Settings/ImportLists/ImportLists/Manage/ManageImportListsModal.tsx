// Phase 27.1 Plan 27.1-04 Task 6 — slim shell. 1:1 mirror of
// frontend/src/Settings/Indexers/Indexers/Manage/ManageIndexersModal.tsx.
// Replaces the Phase 26 Plan 26-05 empty-state placeholder per D-03.
import React from 'react';
import Modal from 'Components/Modal/Modal';
import ManageImportListsModalContent from './ManageImportListsModalContent';

interface ManageImportListsModalProps {
  isOpen: boolean;
  onModalClose(): void;
}

function ManageImportListsModal(props: ManageImportListsModalProps) {
  const { isOpen, onModalClose } = props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <ManageImportListsModalContent onModalClose={onModalClose} />
    </Modal>
  );
}

export default ManageImportListsModal;
