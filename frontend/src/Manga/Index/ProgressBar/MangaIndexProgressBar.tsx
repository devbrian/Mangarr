// Sonarr divergence: Phase 15 Plan 15-12 — Utilities/Series/getProgressBarKind
// inlined below per cascade absorption (Plan 15-07 deleted the Utilities/Series/
// subtree). Behaviour preserved verbatim from Phase 7 Plan 07-04 (status union
// extended for manga statuses).
import React from 'react';
import { useQueueDetailsForSeries } from 'Activity/Queue/Details/QueueDetailsProvider';
import ProgressBar from 'Components/ProgressBar';
import { kinds, sizes } from 'Helpers/Props';
import { MangaStatus } from 'Manga/Manga';
import translate from 'Utilities/String/translate';
import styles from './MangaIndexProgressBar.css';

function getProgressBarKind(
  status: string,
  monitored: boolean,
  progress: number,
  isDownloading: boolean
) {
  if (isDownloading) {
    return kinds.PURPLE;
  }

  if (progress === 100) {
    return status === 'ended' ||
      status === 'completed' ||
      status === 'cancelled'
      ? kinds.SUCCESS
      : kinds.PRIMARY;
  }

  if (monitored) {
    return kinds.DANGER;
  }

  return kinds.WARNING;
}

interface MangaIndexProgressBarProps {
  mangaId: number;
  seasonNumber?: number;
  monitored: boolean;
  status: MangaStatus;
  episodeCount: number;
  episodeFileCount: number;
  totalEpisodeCount: number;
  width: number;
  detailedProgressBar: boolean;
  isStandalone: boolean;
}

function MangaIndexProgressBar(props: MangaIndexProgressBarProps) {
  const {
    mangaId,
    seasonNumber,
    monitored,
    status,
    episodeCount,
    episodeFileCount,
    totalEpisodeCount,
    width,
    detailedProgressBar,
    isStandalone,
  } = props;

  const queueDetails = useQueueDetailsForSeries(mangaId, seasonNumber);

  const newDownloads = queueDetails.count - queueDetails.episodesWithFiles;
  const progress = episodeCount ? (episodeFileCount / episodeCount) * 100 : 100;
  const text = newDownloads
    ? `${episodeFileCount} + ${newDownloads} / ${episodeCount}`
    : `${episodeFileCount} / ${episodeCount}`;

  return (
    <ProgressBar
      className={styles.progressBar}
      containerClassName={isStandalone ? undefined : styles.progress}
      progress={progress}
      kind={getProgressBarKind(
        status,
        monitored,
        progress,
        queueDetails.count > 0
      )}
      size={detailedProgressBar ? sizes.MEDIUM : sizes.SMALL}
      showText={detailedProgressBar}
      text={text}
      title={translate('MangaProgressBarText', {
        chapterFileCount: episodeFileCount,
        chapterCount: episodeCount,
        totalChapterCount: totalEpisodeCount,
        downloadingCount: queueDetails.count,
      })}
      width={width}
    />
  );
}

export default MangaIndexProgressBar;
