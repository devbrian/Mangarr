// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Episode/EpisodeNumber.tsx (Series-shape
// number formatter; manga sibling drops Season + scene numbering, adds
// optional volume label).
//
// Manga sibling preserves: small inline-formatter pattern (no external deps).
// Manga sibling diverges from EpisodeNumber:
//   * No SeasonNumber, SceneSeason / SceneEpisode / SceneAbsolute, AnimeType,
//     AlternateTitles popover (PROJECT.md Volumes/Seasons + Scene-numbering
//     Out-of-Scope).
//   * `chapterNumber` is decimal-aware (Phase 2 D-12 widened DB column to
//     DECIMAL(10,3)). Display logic: integer-shorthand for whole numbers;
//     fractional-trimmed for decimal values (e.g. 1.5 not 1.500).
//   * Volume label `Vol. N` rendered inline before the chapter number when
//     `volumeNumber` is provided (display-only — there is no Volumes table).
//
// Phase 8 cleanup: collapse with EpisodeNumber when Tv/ deletes.
import React from 'react';

export interface ChapterNumberProps {
  chapterNumber: number;
  absoluteChapterNumber?: number;
  volumeNumber?: number;
  showVolumeNumber?: boolean;
}

/**
 * Format a decimal chapter number for display:
 *   1     →  '1'
 *   1.5   →  '1.5'
 *   1.123 →  '1.123'
 *   1.500 →  '1.5'
 *
 * Uses Number.isInteger to short-circuit the common case; decimals are
 * formatted to 3 places and trimmed of trailing zeroes / dots.
 */
function formatChapterNumber(chapterNumber: number): string {
  if (Number.isInteger(chapterNumber)) {
    return chapterNumber.toString();
  }

  return chapterNumber
    .toFixed(3)
    .replace(/0+$/, '')
    .replace(/\.$/, '');
}

function ChapterNumber({
  chapterNumber,
  absoluteChapterNumber,
  volumeNumber,
  showVolumeNumber = false,
}: ChapterNumberProps) {
  const display = formatChapterNumber(chapterNumber);

  return (
    <span>
      {showVolumeNumber && volumeNumber != null ? (
        <span>{`Vol. ${volumeNumber} `}</span>
      ) : null}

      {display}

      {absoluteChapterNumber != null && absoluteChapterNumber !== chapterNumber ? (
        <span>{` (${formatChapterNumber(absoluteChapterNumber)})`}</span>
      ) : null}
    </span>
  );
}

export default ChapterNumber;
