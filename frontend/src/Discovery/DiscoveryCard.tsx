// Phase 42 Plan 42-07 — Discovery result card (sketch 001, polished).
//
// NEW-in-Mangarr surface (no Sonarr analog); the closest peer is
// AddManga/AddNewManga/AddNewMangaSearchResult.tsx (poster + title + badges).
// Renders one MangaBaka browse result as a poster-grid card:
//   - cover with a type badge (top-left) + ★ score badge (top-right), a
//     click-to-open Tags popover (bottom-left) + a hover-reveal Exclude ✕
//     (bottom-right);
//   - body with a click-to-open description popover on the title, a
//     per-status-colored status badge + year meta row, and a genre line.
//
// T-42-07-XSS: the MangaBaka title / type / status / genre / tag / description
// strings are rendered via plain JSX ({...}) which React auto-escapes — never
// via a raw-HTML inject (T-07-12 precedent; pinned by the 42-07 acceptance grep).
import classNames from 'classnames';
import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { DiscoveryResult } from 'Discovery/DiscoveryModels';
import DiscoveryPopover from 'Discovery/DiscoveryPopover';
import { icons } from 'Helpers/Props';
import * as tooltipPositions from 'Helpers/Props/tooltipPositions';
import translate from 'Utilities/String/translate';
import styles from './DiscoveryCard.css';

interface DiscoveryCardProps {
  result: DiscoveryResult;
  onExclude: (result: DiscoveryResult) => void;
}

// Sketch 001 shows up to three genres on the card; keep the line tidy.
const MAX_GENRES = 3;

// Per-status badge colour (the user asked for distinct colours per status).
const STATUS_CLASS: Record<string, string> = {
  releasing: styles.statusReleasing,
  completed: styles.statusCompleted,
  hiatus: styles.statusHiatus,
  cancelled: styles.statusCancelled,
  upcoming: styles.statusUpcoming,
  unknown: styles.statusUnknown,
};

function DiscoveryCard({ result, onExclude }: DiscoveryCardProps) {
  const {
    title,
    coverUrl,
    year,
    score,
    type,
    status,
    genres,
    tags,
    description,
    mangaBakaId,
  } = result;

  const handleExcludePress = useCallback(() => {
    onExclude(result);
  }, [onExclude, result]);

  // MangaBaka score is a 0–100 fractional value; the sketch shows it as ★ x.x
  // out of 10.
  const scoreLabel =
    score != null ? `★ ${(Number(score) / 10).toFixed(1)}` : null;

  const genreLine = genres?.slice(0, MAX_GENRES).join(' · ');
  const statusClass = status
    ? STATUS_CLASS[status.toLowerCase()] ?? styles.statusUnknown
    : styles.statusUnknown;

  const titleNode = (
    <div className={styles.title} title={title}>
      {title}
    </div>
  );

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

        {tags && tags.length > 0 ? (
          <div className={styles.coverTags}>
            <DiscoveryPopover
              className={styles.tagsButton}
              position={tooltipPositions.TOP}
              anchor={
                <span
                  className={styles.tagsButtonInner}
                  data-testid={`discovery-card-tags-${mangaBakaId}`}
                >
                  <Icon name={icons.TAGS} size={12} />
                  <span className={styles.tagsCount}>{tags.length}</span>
                </span>
              }
              title={translate('Tags')}
              body={
                <div className={styles.tagsPopover}>
                  {tags.map((tag) => (
                    <span key={tag} className={styles.tagPill}>
                      {tag}
                    </span>
                  ))}
                </div>
              }
            />
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
        {description ? (
          <DiscoveryPopover
            className={styles.titlePopoverAnchor}
            position={tooltipPositions.TOP}
            anchor={titleNode}
            title={title}
            body={
              <div className={styles.descriptionPopover}>{description}</div>
            }
          />
        ) : (
          titleNode
        )}

        <div className={styles.meta}>
          {status ? (
            <span className={classNames(styles.badge, statusClass)}>
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
