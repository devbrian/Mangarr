import React from 'react';
import HeartRating from 'Components/HeartRating';
import MangaTagList from 'Components/MangaTagList';
// Phase 17.3 D-14: useCountryName / Language imports dropped — the
// `sortKey === 'network'/'originalCountry'/'originalLanguage'/'previousAiring'`
// branches that consumed them were deleted (no airing concept; no network
// in manga domain per DOMAIN-01/02). D-08 orphan-import sweep applied.
// formatDateTime / getRelativeDate kept — still used by the `'added'` branch.
import { Ratings } from 'Manga/Manga';
import { QualityProfileModel } from 'Settings/Profiles/Quality/useQualityProfiles';
import formatDateTime from 'Utilities/Date/formatDateTime';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './MangaIndexPosterInfo.css';

// Phase 17.3 D-14: originalCountry/originalLanguage/network/previousAiring/
// seasonCount props dropped from MangaIndexPosterInfoProps (no longer on
// Manga interface per D-13; manga has no seasons per DOMAIN-02). The parent
// MangaIndexPoster.tsx call site was forked to match.
interface MangaIndexPosterInfoProps {
  showQualityProfile: boolean;
  qualityProfile?: QualityProfileModel;
  added?: string;
  path: string;
  sizeOnDisk?: number;
  ratings?: Ratings;
  tags: number[];
  sortKey: string;
  showRelativeDates: boolean;
  shortDateFormat: string;
  longDateFormat: string;
  timeFormat: string;
  showTags: boolean;
}

function MangaIndexPosterInfo(props: MangaIndexPosterInfoProps) {
  const {
    qualityProfile,
    showQualityProfile,
    added,
    path,
    sizeOnDisk = 0,
    ratings,
    tags,
    sortKey,
    showRelativeDates,
    shortDateFormat,
    longDateFormat,
    timeFormat,
    showTags,
  } = props;

  // Phase 17.3 D-14: `sortKey === 'network'` / `'originalCountry'` /
  // `'originalLanguage'` / `'previousAiring'` branches deleted (no airing
  // concept; no network in manga domain). The Options modal sort-key sweep
  // is Plan 17.3-13 territory.

  if (
    sortKey === 'qualityProfileId' &&
    !showQualityProfile &&
    !!qualityProfile?.name
  ) {
    return (
      <div className={styles.info} title={translate('QualityProfile')}>
        {qualityProfile.name}
      </div>
    );
  }

  if (sortKey === 'added' && added) {
    const addedDate = getRelativeDate({
      date: added,
      shortDateFormat,
      showRelativeDates,
      timeFormat,
      timeForToday: false,
    });

    return (
      <div
        className={styles.info}
        title={formatDateTime(added, longDateFormat, timeFormat)}
      >
        {translate('Added')}: {addedDate}
      </div>
    );
  }

  // Phase 17.3 D-14: `sortKey === 'seasonCount'` branch deleted (manga has
  // no seasons per DOMAIN-02). The Options modal sort-key sweep is Plan
  // 17.3-13 territory.

  if (!showTags && sortKey === 'tags' && tags.length) {
    return (
      <div className={styles.tags}>
        <div className={styles.tagsList}>
          <MangaTagList tags={tags} />
        </div>
      </div>
    );
  }

  if (sortKey === 'path') {
    return (
      <div className={styles.info} title={translate('Path')}>
        {path}
      </div>
    );
  }

  if (sortKey === 'sizeOnDisk') {
    return (
      <div className={styles.info} title={translate('SizeOnDisk')}>
        {formatBytes(sizeOnDisk)}
      </div>
    );
  }

  if (sortKey === 'ratings' && ratings?.value) {
    return (
      <div className={styles.info} title={translate('Rating')}>
        <HeartRating rating={ratings.value} votes={ratings.votes} />
      </div>
    );
  }

  return null;
}

export default MangaIndexPosterInfo;
