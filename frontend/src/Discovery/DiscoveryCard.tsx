// Phase 42 Plan 42-07 — Discovery result card (sketch 001, 1:1).
//
// NEW-in-Mangarr surface (no Sonarr analog); the closest peer is
// AddManga/AddNewManga/AddNewMangaSearchResult.tsx (poster + title + badges).
// Renders one MangaBaka browse result as a poster-grid card matching sketch 001:
//   - cover with a type badge (top-left) + ★ score badge (top-right) + a
//     hover-reveal Exclude ✕ (bottom-right);
//   - body with a 2-line title, a status badge + year meta row, and a genre line.
// The Exclude action drops the card from the grid instantly (handled by the
// parent), writes the GLOBAL ImportListExclusion optimistically, and surfaces an
// Undo (both owned by Discovery.tsx via onExclude).
//
// T-42-07-XSS: the MangaBaka title / type / status / genre strings are rendered
// via plain JSX ({...}) which React auto-escapes — never via a raw-HTML inject
// (T-07-12 precedent; pinned by the 42-07 acceptance grep == 0).
import classNames from 'classnames';
import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { DiscoveryResult } from 'Discovery/DiscoveryModels';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './DiscoveryCard.css';

interface DiscoveryCardProps {
  result: DiscoveryResult;
  onExclude: (result: DiscoveryResult) => void;
}

// Sketch 001 shows up to three genres on the card; keep the line tidy.
const MAX_GENRES = 3;

function DiscoveryCard({ result, onExclude }: DiscoveryCardProps) {
  const { title, coverUrl, year, score, type, status, genres, mangaBakaId } =
    result;

  const handleExcludePress = useCallback(() => {
    onExclude(result);
  }, [onExclude, result]);

  // MangaBaka score is a 0–100 fractional value; the sketch shows it as ★ x.x
  // out of 10.
  const scoreLabel =
    score != null ? `★ ${(Number(score) / 10).toFixed(1)}` : null;

  const genreLine = genres?.slice(0, MAX_GENRES).join(' · ');

  return (
    <div className={styles.card} data-testid={`discovery-card-${mangaBakaId}`}>
      <div className={styles.posterContainer}>
        {coverUrl ? (
          <img className={styles.poster} src={coverUrl} alt={title} />
        ) : (
          <div className={styles.posterPlaceholder}>
            <Icon name={icons.POSTER} size={45} />
          </div>
        )}

        {type ? (
          <div className={styles.coverBadges}>
            <span className={classNames(styles.badge, styles.badgeType)}>
              {type}
            </span>
          </div>
        ) : null}

        {scoreLabel ? (
          <div className={styles.coverScore}>
            <span className={classNames(styles.badge, styles.badgeScore)}>
              {scoreLabel}
            </span>
          </div>
        ) : null}

        <Link
          className={styles.excludeButton}
          data-testid={`discovery-card-exclude-${mangaBakaId}`}
          aria-label={translate('Exclude')}
          title={translate('Exclude')}
          onPress={handleExcludePress}
        >
          <Icon name={icons.REMOVE} size={13} />
        </Link>
      </div>

      <div className={styles.body}>
        <div className={styles.title} title={title}>
          {title}
        </div>

        <div className={styles.meta}>
          {status ? (
            <span className={classNames(styles.badge, styles.badgeStatus)}>
              {status}
            </span>
          ) : null}
          {year ? <span>{year}</span> : null}
        </div>

        {genreLine ? <div className={styles.genreLine}>{genreLine}</div> : null}
      </div>
    </div>
  );
}

export default DiscoveryCard;
