// Phase 42 Plan 42-07 — Discovery result card (sketch 001).
//
// NEW-in-Mangarr surface (no Sonarr analog); the closest peer is
// AddManga/AddNewManga/AddNewMangaSearchResult.tsx (poster + title + badges).
// Renders one MangaBaka browse result as a poster-grid card with a hover Exclude
// ✕. The Exclude action drops the card from the grid instantly (handled by the
// parent), writes the GLOBAL ImportListExclusion optimistically, and surfaces an
// Undo (both owned by Discovery.tsx via onExclude).
//
// T-42-07-XSS: the MangaBaka title / type / status strings are rendered via plain
// JSX ({result.title}) which React auto-escapes — never via a raw-HTML inject
// (T-07-12 precedent; pinned by the 42-07 acceptance grep == 0).
import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { DiscoveryResult } from 'Discovery/DiscoveryModels';
import { icons, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './DiscoveryCard.css';

interface DiscoveryCardProps {
  result: DiscoveryResult;
  onExclude: (result: DiscoveryResult) => void;
}

function DiscoveryCard({ result, onExclude }: DiscoveryCardProps) {
  const { title, coverUrl, year, score, type, mangaBakaId } = result;

  const handleExcludePress = useCallback(() => {
    onExclude(result);
  }, [onExclude, result]);

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

        <Link
          className={styles.excludeButton}
          data-testid={`discovery-card-exclude-${mangaBakaId}`}
          aria-label={translate('Exclude')}
          title={translate('Exclude')}
          onPress={handleExcludePress}
        >
          <Icon name={icons.REMOVE} size={14} />
        </Link>
      </div>

      <div className={styles.title} title={title}>
        {title}
      </div>

      <div className={styles.badges}>
        {year ? <Label size={sizes.MEDIUM}>{year}</Label> : null}

        {score != null ? (
          <Label kind={kinds.INFO} size={sizes.MEDIUM}>
            {score}
          </Label>
        ) : null}

        {type ? (
          <Label kind={kinds.DEFAULT} size={sizes.MEDIUM}>
            {type}
          </Label>
        ) : null}
      </div>
    </div>
  );
}

export default DiscoveryCard;
