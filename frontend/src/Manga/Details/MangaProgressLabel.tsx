// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/SeriesProgressLabel.tsx
// (Episode counts → chapter counts; queueDetails sibling deferred).
//
// Manga sibling preserves: kind-by-progress branching shape.
// Manga sibling diverges from SeriesProgressLabel:
//   * Reads chapter counts (chapterFileCount / chapterCount) instead of
//     episode counts.
//   * No `useQueueDetailsForSeries` integration in v1 — the per-manga queue
//     overlay (purple-while-downloading) is deferred. Plan 07-09 wires it.
//
// Phase 8 cleanup: collapse with SeriesProgressLabel when Tv/ deletes.
import React from 'react';
import Label from 'Components/Label';
import { kinds, sizes } from 'Helpers/Props';

function getChapterCountKind(
  monitored: boolean,
  chapterFileCount: number,
  chapterCount: number
) {
  if (chapterFileCount === chapterCount && chapterCount > 0) {
    return kinds.SUCCESS;
  }

  if (!monitored) {
    return kinds.WARNING;
  }

  return kinds.DANGER;
}

interface MangaProgressLabelProps {
  className?: string;
  monitored: boolean;
  chapterCount: number;
  chapterFileCount: number;
}

function MangaProgressLabel({
  className,
  monitored,
  chapterCount,
  chapterFileCount,
}: MangaProgressLabelProps) {
  const text = `${chapterFileCount} / ${chapterCount}`;

  return (
    <Label
      className={className}
      kind={getChapterCountKind(monitored, chapterFileCount, chapterCount)}
      size={sizes.LARGE}
    >
      <span>{text}</span>
    </Label>
  );
}

export default MangaProgressLabel;
