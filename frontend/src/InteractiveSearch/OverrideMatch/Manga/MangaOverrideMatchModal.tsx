// Sonarr divergence: NEW manga sibling per Phase 12 Plan 12-10 sub-wave-B-addition (audit row C closure) — see DIVERGENCE.md.
// Role-match analog: frontend/src/InteractiveSearch/OverrideMatch/OverrideMatchModal.tsx (TV-shape sibling — preserved verbatim per D-12-18).
// Source: .planning/phases/08-tv-manga-parity-audit/audit/OverrideMatch-vs-MangaPayloadRouting.md ## Backfill outline item 1.
//
// Manga sibling preserves: Modal wrapper shape — Modal + onModalClose + sizes.LARGE convention.
//
// Manga sibling diverges from OverrideMatchModal:
//   * Body component is MangaOverrideMatchModalContent (NOT OverrideMatchModalContent — manga-shaped props).
//   * Props payload is manga-shaped (chapterIds + mangaId + scanlationGroup? + translatedLanguage? + downloadClientId? + grab plumbing) — no seriesId / seasonNumber / episodes / quality / languages.
//
// Phase 15 cleanup: when TV-side OverrideMatchModal is deleted, this manga sibling renames + flattens to OverrideMatch/OverrideMatchModal.tsx as the canonical default.
import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import MangaOverrideMatchModalContent, {
  MangaOverrideMatchModalContentProps,
} from './MangaOverrideMatchModalContent';

interface MangaOverrideMatchModalProps
  extends MangaOverrideMatchModalContentProps {
  isOpen: boolean;
}

function MangaOverrideMatchModal({
  isOpen,
  onModalClose,
  ...otherProps
}: MangaOverrideMatchModalProps) {
  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={onModalClose}>
      <MangaOverrideMatchModalContent
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default MangaOverrideMatchModal;
