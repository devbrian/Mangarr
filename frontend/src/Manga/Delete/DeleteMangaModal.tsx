// Sonarr divergence: NEW manga sibling per Phase 15 Plan 15-12 deferred
// "v1.1+ dedicated single-manga Delete modal". Role-match analog:
// frontend/src/Series/Delete/DeleteSeriesModal.tsx (deleted in Plan 15-07
// cascade; the on-disk file at that path is now a `() => null` stub since
// Plan 15-12, so it is NOT a useful structural reference).
//
// Manga sibling preserves: thin Modal wrapper that mounts/unmounts
// DeleteMangaModalContent in response to isOpen — mirrors EditMangaModal's
// shape (PR #27 — fix(manga-edit-button-no-op)) one-for-one. The
// EditMangaModal is the closest live reference for this pattern.
//
// Manga sibling diverges from EditMangaModal: closes the manga-detail Delete
// modal mount instead of the Edit one (the only call-site difference at this
// layer is the imported content component).
import React from 'react';
import Modal from 'Components/Modal/Modal';
import DeleteMangaModalContent, {
  DeleteMangaModalContentProps,
} from './DeleteMangaModalContent';

interface DeleteMangaModalProps extends DeleteMangaModalContentProps {
  isOpen: boolean;
}

function DeleteMangaModal({
  isOpen,
  mangaId,
  onModalClose,
}: DeleteMangaModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <DeleteMangaModalContent mangaId={mangaId} onModalClose={onModalClose} />
    </Modal>
  );
}

export default DeleteMangaModal;
