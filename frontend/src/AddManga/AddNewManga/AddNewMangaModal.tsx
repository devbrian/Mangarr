// Sonarr divergence: NEW manga sibling per Phase 7 D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/AddSeries/AddNewSeries/AddNewSeriesModal.tsx
// (thin wrapper around AddNewSeriesModalContent).
//
// Manga sibling preserves: thin Modal wrapper invoking the content component.
// Manga sibling diverges: passes AddManga-shape result to AddNewMangaModalContent.
//
// Phase 8 cleanup: collapse with AddNewSeriesModal when AddSeries/ deletes.
import React from 'react';
import Modal from 'Components/Modal/Modal';
import AddNewMangaModalContent, {
  AddNewMangaModalContentProps,
} from './AddNewMangaModalContent';

interface AddNewMangaModalProps extends AddNewMangaModalContentProps {
  isOpen: boolean;
}

function AddNewMangaModal({
  isOpen,
  onModalClose,
  ...otherProps
}: AddNewMangaModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <AddNewMangaModalContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default AddNewMangaModal;
