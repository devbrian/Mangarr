// Sonarr divergence: NEW manga sibling per Phase 15 Plan 15-12 deferred
// "v1.1+ dedicated single-manga Edit modal". Role-match analog:
// frontend/src/Series/Edit/EditSeriesModal.tsx (deleted in Plan 15-07;
// canonical reference is git commit 0521a6c39 — pre-Tv-cleanup).
//
// Manga sibling preserves: thin Modal wrapper that mounts/unmounts
// EditMangaModalContent in response to isOpen.
// Manga sibling diverges from EditSeriesModal:
//   * No Redux dispatch on close (clearPendingChanges is irrelevant —
//     usePendingChangesStore is now Zustand-local; pending changes evaporate
//     when the content component unmounts).
import React from 'react';
import Modal from 'Components/Modal/Modal';
import EditMangaModalContent, {
  EditMangaModalContentProps,
} from './EditMangaModalContent';

interface EditMangaModalProps extends EditMangaModalContentProps {
  isOpen: boolean;
}

function EditMangaModal({
  isOpen,
  mangaId,
  onModalClose,
}: EditMangaModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <EditMangaModalContent mangaId={mangaId} onModalClose={onModalClose} />
    </Modal>
  );
}

export default EditMangaModal;
