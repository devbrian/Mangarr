// Sonarr divergence: NEW per Phase 25 Plan 25-04 Task 5 (v1.1-04
// OverrideMatch split) — see DIVERGENCE.md.
//
// Manga-shape sibling of OverrideMatchModal — chapter-specific flavor
// (mangaId + REQUIRED non-empty chapterIds). The Plan-12-10-shipped
// MangaOverrideMatchModal carried both shapes via sentinel logic
// (mangaId=0 + chapterIds=[N] meant "chapter search"; mangaId=N +
// chapterIds=[] meant "manga search"). Plan 25-04 splits these into
// two siblings — Chapter (this file) + Manga (narrowed in Task 6).
//
// 25-hotfix (UAT item 4 / WR-04): The wire-level `mangaId=0` sentinel is
// PRESERVED at the InteractiveSearchRow.tsx call site even after the typed
// split. The split decommissions the RUNTIME shape-detection (no more
// `chapterIds?.length` property-presence checks) but the request body still
// carries `mangaId: 0` for chapter-flavored overrides because the backend
// `MangaReleaseController` accepts that as the documented chapter-flavored
// override shape. If the backend ever tightens validation to reject
// `mangaId=0`, the InteractiveSearchRow.tsx:406 call site MUST switch to
// option (a) in 25-REVIEW.md §WR-04 (omit mangaId from the override payload
// via conditional spread). See 25-REVIEW.md §WR-04 for the full disposition.
//
// Wrapper pattern copied from MangaOverrideMatchModal.tsx (Modal + sizes.LARGE).
import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import ChapterOverrideMatchModalContent, {
  ChapterOverrideMatchModalContentProps,
} from './ChapterOverrideMatchModalContent';

interface ChapterOverrideMatchModalProps
  extends ChapterOverrideMatchModalContentProps {
  isOpen: boolean;
}

function ChapterOverrideMatchModal({
  isOpen,
  onModalClose,
  ...otherProps
}: ChapterOverrideMatchModalProps) {
  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={onModalClose}>
      <ChapterOverrideMatchModalContent
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default ChapterOverrideMatchModal;
