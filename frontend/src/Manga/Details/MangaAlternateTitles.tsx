// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/SeriesAlternateTitles.tsx
// (verbatim port — manga shares the AlternateTitle entity shape).
//
// Manga sibling preserves: <ul> + comment-suffix render shape.
// Manga sibling diverges from SeriesAlternateTitles: identifier renames only.
//
// Phase 8 cleanup: collapse with SeriesAlternateTitles when Tv/ deletes.
import React from 'react';
import { AlternateTitle } from 'Manga/Manga';
import styles from './MangaAlternateTitles.css';

interface MangaAlternateTitlesProps {
  alternateTitles: AlternateTitle[];
}

function MangaAlternateTitles({ alternateTitles }: MangaAlternateTitlesProps) {
  return (
    <ul>
      {alternateTitles.map((alternateTitle) => {
        return (
          <li key={alternateTitle.title} className={styles.alternateTitle}>
            {alternateTitle.title}
            {alternateTitle.comment ? (
              <span className={styles.comment}> {alternateTitle.comment}</span>
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}

export default MangaAlternateTitles;
