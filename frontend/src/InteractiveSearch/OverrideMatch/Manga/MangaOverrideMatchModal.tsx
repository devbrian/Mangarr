// Sonarr divergence: NEW manga sibling per Phase 12 Plan 12-10 sub-wave-B-addition (audit row C closure) — see DIVERGENCE.md.
// Source: .planning/phases/08-tv-manga-parity-audit/audit/OverrideMatch-vs-MangaPayloadRouting.md ## Backfill outline item 1.
// The TV-shape OverrideMatchModal sibling was retired in issue #263.
//
// Modal wrapper shape: Modal + onModalClose + sizes.LARGE convention.
//   * Body component is MangaOverrideMatchModalContent (manga-shaped props).
//   * Props payload is manga-shaped (mangaId + scanlationGroup? + translatedLanguage? + downloadClientId? + grab plumbing) — no seriesId / seasonNumber / episodes / quality / languages. Plan 25-04 Task 6 (v1.1-04 narrow) dropped the chapterIds prop entirely; the chapter-flavored search now uses the sibling ChapterOverrideMatchModal (Task 5).
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
