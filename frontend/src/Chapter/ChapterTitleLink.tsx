// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/EpisodeTitleLink.tsx (verbatim
// shape with Episode → Chapter rename + drop FinaleType).
//
// Manga sibling preserves: Link-button → modal-open flow.
// Manga sibling diverges from EpisodeTitleLink:
//   * No `finaleType` (manga has no finale concept v1).
//   * Opens ChapterDetailsModal (which is search-first per the Plan 07-05
//     v1 simplification — see ChapterDetailsModal.tsx header).
//
// Phase 8 cleanup: collapse with EpisodeTitleLink when Tv/ deletes.
import React, { useCallback, useState } from 'react';
import Link from 'Components/Link/Link';
import ChapterDetailsModal from './ChapterDetailsModal';

export interface ChapterTitleLinkProps {
  chapterId: number;
  mangaId: number;
  chapterTitle?: string;
  chapterNumber?: number;
}

function ChapterTitleLink({
  chapterId,
  mangaId,
  chapterTitle,
  chapterNumber,
}: ChapterTitleLinkProps) {
  const [isOpen, setIsOpen] = useState(false);
  const handlePress = useCallback(() => setIsOpen(true), []);
  const handleClose = useCallback(() => setIsOpen(false), []);

  // Synthesized chapters (e.g. MangaBaka, which ships only a chapter COUNT and
  // no per-chapter feed) carry no Title. Rendering an empty Link produces a
  // zero-width, unclickable button — the user can't open the details modal.
  // Fall back to "Chapter {number}" so the link always has clickable content.
  const displayLabel =
    chapterTitle || (chapterNumber != null ? `Chapter ${chapterNumber}` : '');

  return (
    <>
      <Link
        data-testid={`chapter-row-${chapterId}-title`}
        onPress={handlePress}
      >
        {displayLabel}
      </Link>

      <ChapterDetailsModal
        isOpen={isOpen}
        chapterId={chapterId}
        mangaId={mangaId}
        chapterTitle={chapterTitle}
        onModalClose={handleClose}
      />
    </>
  );
}

export default ChapterTitleLink;
