// Phase 27.1 Plan 27.1-04 Task 5 — slim Tags modal shell. 1:1 mirror of
// frontend/src/Settings/Indexers/Indexers/Manage/Tags/TagsModal.tsx per
// PATTERNS §Manage Subtree. No swaps; the bulk-tag UX is domain-agnostic.
import React from 'react';
import Modal from 'Components/Modal/Modal';
import TagsModalContent from './TagsModalContent';

interface TagsModalProps {
  isOpen: boolean;
  ids: number[];
  onApplyTagsPress: (tags: number[], applyTags: string) => void;
  onModalClose: () => void;
}

function TagsModal(props: TagsModalProps) {
  const { isOpen, onModalClose, ...otherProps } = props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <TagsModalContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default TagsModal;
