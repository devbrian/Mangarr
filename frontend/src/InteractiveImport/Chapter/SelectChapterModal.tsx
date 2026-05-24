// Sonarr divergence: Phase 30 Plan 30-03 (II2-01) — see DIVERGENCE.md.
// Role-match analog: Sonarr v5-develop
// `frontend/src/InteractiveImport/Episode/SelectEpisodeModal.tsx` —
// line-by-line outer-modal wrapper port with Episode -> Chapter substitution
// and R-5 aggressive strip (no Season ordinal / anime-format / multi-episode-per-file
// props — manga has no Season per PROJECT.md DOMAIN-02 and is 1 CBZ = 1
// chapter per Phase 4 ARCHIVE invariant).
//
// Replaces the Plan-15-12 `return null` stub (carried through Plan 25-04
// Task 1 subdir rename `Episode/` -> `Chapter/`). Inner Body lives in the
// sibling `SelectChapterModalContent.tsx` per Sonarr's Modal + ModalContent
// split convention. SelectedChapter shape (Plan 30-01 II2-04 rename target)
// re-exported from the content module.
import React from 'react';
import Modal from 'Components/Modal/Modal';
import SelectChapterModalContent, {
  SelectedChapter,
} from './SelectChapterModalContent';

interface SelectChapterModalProps {
  isOpen: boolean;
  selectedIds: number[] | string[];
  mangaId?: number;
  // R-5 strip: no Season ordinal (no Season in manga) / anime-format (no scene
  // numbering) props relative to Sonarr's SelectEpisodeModal.
  selectedDetails?: string;
  modalTitle: string;
  // Sonarr peer types onEpisodesSelect as required. Mangarr keeps it
  // optional because the OverrideMatch consumer (frontend/src/
  // InteractiveSearch/OverrideMatch/OverrideMatchModalContent.tsx:342)
  // renders the modal without a callback today — Plan 30-01 Task 2
  // (II2-05) deleted the dead onEpisodesSelect wiring there but kept the
  // JSX element in scope-deferral per CONTEXT.md `<deferred>`
  // ("OverrideMatchModalContent broader refactor"). When the broader
  // refactor lands in v1.3+ the prop can be promoted back to required.
  onChaptersSelect?(selectedChapters: SelectedChapter[]): void;
  onModalClose(): void;
}

function SelectChapterModal(props: SelectChapterModalProps) {
  const {
    isOpen,
    selectedIds,
    mangaId,
    selectedDetails,
    modalTitle,
    onChaptersSelect,
    onModalClose,
  } = props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <SelectChapterModalContent
        selectedIds={selectedIds}
        mangaId={mangaId}
        selectedDetails={selectedDetails}
        modalTitle={modalTitle}
        onChaptersSelect={onChaptersSelect}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default SelectChapterModal;
