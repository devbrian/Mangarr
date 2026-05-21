// Phase 27.1 Plan 27.1-04 Task 4 — slim Edit modal shell. 1:1 mirror of
// frontend/src/Settings/Indexers/Indexers/Manage/Edit/ManageIndexersEditModal.tsx
// per PATTERNS §Manage Subtree. Only swap: indexerIds → importListIds.
import React from 'react';
import Modal from 'Components/Modal/Modal';
import ManageImportListsEditModalContent from './ManageImportListsEditModalContent';

interface ManageImportListsEditModalProps {
  isOpen: boolean;
  importListIds: number[];
  onSavePress(payload: object): void;
  onModalClose(): void;
}

function ManageImportListsEditModal(props: ManageImportListsEditModalProps) {
  const { isOpen, importListIds, onSavePress, onModalClose } = props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <ManageImportListsEditModalContent
        importListIds={importListIds}
        onSavePress={onSavePress}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default ManageImportListsEditModal;
