// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/NoSeries.tsx (verbatim port).
//
// Manga sibling preserves: filtered-message branch + empty-state branch
// + button container styling (reuses Series/NoSeries.css module verbatim).
// Manga sibling diverges from NoSeries:
//   * Empty-state heading + body copy locked by UI-SPEC §Empty States
//     (Plan 07-04 must_haves §truths + §UI-SPEC).
//   * CTA links to '/add/manga' (Plan 07-04 D-09 additive route).
//
// Phase 8 cleanup: collapse with NoSeries when Tv/ deletes.
import React from 'react';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
// Sonarr divergence: Phase 15 Plan 15-12 — Series/NoSeries.css copied to Manga/NoManga.css
// (Plan 15-07 deleted Series subtree). CSS class names + sizes preserved verbatim.
import styles from './NoManga.css';

interface NoMangaProps {
  totalItems: number;
}

function NoManga(props: NoMangaProps) {
  const { totalItems } = props;

  if (totalItems > 0) {
    return (
      <div>
        <div className={styles.message}>
          {translate('AllMangaAreHiddenByTheAppliedFilter')}
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className={styles.message}>{translate('NoMangaAddedYet')}</div>

      <div className={styles.buttonContainer}>
        <Button to="/add/manga" kind={kinds.PRIMARY}>
          {translate('AddNewManga')}
        </Button>
      </div>
    </div>
  );
}

export default NoManga;
