// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/EpisodeDetailsModal.tsx
// (Modal-wrapper-around-ModalContent shape; the Episode equivalent has a
// 3-tab content panel — Details / History / Search — with a complex
// react-tabs structure tying back into per-episode hooks).
//
// Manga sibling preserves: Modal-wrapper shape; sizes.EXTRA_EXTRA_LARGE
// matches Episode default.
// Manga sibling diverges from EpisodeDetailsModal:
//   * v1 ships a streamlined search-first modal — opening always lands on
//     the InteractiveSearch panel (Phase 6 D-08 ranked-modal pattern, reused
//     verbatim per Plan 07-05 Lock #14). Per-chapter Details / History tabs
//     are deferred to a future plan; v1 shows a small chapter header strip
//     above the InteractiveSearch table for context.
//   * Plumbed via the URL-shaped React Query cache (`['/chapter']`) Plan
//     07-02 contract — no EpisodeEntity-style discriminator.
//   * No `EpisodeDetailsTab` enum; the modal only has the search affordance
//     in v1. Once Plans 07-08+ ship Files / History tab content, this modal
//     can grow tabs.
//
// Phase 8 cleanup: collapse with EpisodeDetailsModal when Tv/ deletes; keep
// the streamlined search-first shape as the canonical Mangarr default.
import React from 'react';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { sizes } from 'Helpers/Props';
import InteractiveSearch from 'InteractiveSearch/InteractiveSearch';
import translate from 'Utilities/String/translate';
import ChapterNumber from './ChapterNumber';
import { useSingleChapter } from './useChapter';

export interface ChapterDetailsModalProps {
  isOpen: boolean;
  chapterId: number;
  mangaId?: number;
  chapterTitle?: string;
  onModalClose(): void;
}

function ChapterDetailsModal({
  isOpen,
  chapterId,
  chapterTitle,
  onModalClose,
}: ChapterDetailsModalProps) {
  const { data: chapter } = useSingleChapter(isOpen ? chapterId : undefined);

  return (
    <Modal
      isOpen={isOpen}
      size={sizes.EXTRA_EXTRA_LARGE}
      closeOnBackgroundClick={false}
      onModalClose={onModalClose}
    >
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          <span data-testid="chapter-details-modal-header">
            {chapter ? (
              <span>
                <ChapterNumber
                  chapterNumber={chapter.chapterNumber}
                  volumeNumber={chapter.volumeNumber}
                  showVolumeNumber={chapter.volumeNumber != null}
                />
                {chapter.title || chapterTitle
                  ? ` — ${chapter.title || chapterTitle}`
                  : null}
              </span>
            ) : (
              chapterTitle ?? translate('Chapter')
            )}
          </span>
        </ModalHeader>

        <ModalBody>
          <InteractiveSearch
            type="chapter"
            searchPayload={{ kind: 'chapter', chapterId }}
          />
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default ChapterDetailsModal;
