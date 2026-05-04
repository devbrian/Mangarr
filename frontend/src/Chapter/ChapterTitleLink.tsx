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
}

function ChapterTitleLink({
  chapterId,
  mangaId,
  chapterTitle,
}: ChapterTitleLinkProps) {
  const [isOpen, setIsOpen] = useState(false);
  const handlePress = useCallback(() => setIsOpen(true), []);
  const handleClose = useCallback(() => setIsOpen(false), []);

  return (
    <>
      <Link onPress={handlePress}>{chapterTitle}</Link>

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
