// Sonarr divergence: Phase 30 Plan 30-03 (II2-01) — see DIVERGENCE.md.
// Role-match analog: Sonarr v5-develop
// `frontend/src/InteractiveImport/Series/SelectSeriesModal.tsx` — line-by-line
// outer-modal wrapper port with Series -> Manga substitution.
//
// Replaces the Plan-15-12 `return null` stub (carried through Plan 25-04
// Task 2 subdir rename `Series/` -> `Manga/`). Inner Body lives in the
// sibling `SelectMangaModalContent.tsx` per Sonarr's Modal + ModalContent
// split convention.
import React, { useCallback } from 'react';
import Modal from 'Components/Modal/Modal';
import Manga from 'Manga/Manga';
import SelectMangaModalContent from './SelectMangaModalContent';

interface SelectMangaModalProps {
  isOpen: boolean;
  modalTitle: string;
  // Sonarr peer types onSeriesSelect as required. Mangarr keeps it optional
  // because the OverrideMatch consumer (frontend/src/InteractiveSearch/
  // OverrideMatch/OverrideMatchModalContent.tsx:333) renders the modal
  // without a callback today — Plan 30-01 Task 2 (II2-05) deleted the dead
  // onSeriesSelect wiring there but kept the JSX element in scope-deferral
  // per CONTEXT.md `<deferred>` ("OverrideMatchModalContent broader
  // refactor"). When OverrideMatch broader refactor lands in v1.3+ the
  // prop can be promoted back to required.
  onMangaSelect?(manga: Manga): void;
  onModalClose(): void;
}

function SelectMangaModal(props: SelectMangaModalProps) {
  const { isOpen, modalTitle, onMangaSelect, onModalClose } = props;

  // Defensive no-op: when consumers (e.g. OverrideMatchModalContent
  // scope-deferred per CONTEXT.md) omit onMangaSelect, picking a row in
  // the modal does nothing beyond closing — the same UX as the prior
  // `return null` stub for those consumers.
  const handleMangaSelect = useCallback(
    (manga: Manga) => {
      if (onMangaSelect) {
        onMangaSelect(manga);
      }
    },
    [onMangaSelect]
  );

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <SelectMangaModalContent
        modalTitle={modalTitle}
        onMangaSelect={handleMangaSelect}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default SelectMangaModal;
