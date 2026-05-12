import classNames from 'classnames';
import React, { useCallback, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import MangaTagList from 'Components/MangaTagList';
import { icons } from 'Helpers/Props';
// fix(home-card-edit-button-no-op): swap stub `Series/Edit/EditSeriesModal`
// (Phase 15 Plan 15-12 `() => null`) for the real per-manga Edit modal
// shipped in PR #27. Sonarr-consistency-audit: mirrors the canonical
// `SeriesIndexPoster.js` (git 909af6c87) modal-mount shape 1:1 modulo
// `seriesId` → `mangaId`. The card overlay has no Delete button (only
// Refresh + optional Search + Edit), so the `DeleteSeriesModal` mount and
// `onDeleteSeriesPress` chain are dropped — they were dead in the stub
// version and would still be dead in the real version.
import EditMangaModal from 'Manga/Edit/EditMangaModal';
import MangaIndexProgressBar from 'Manga/Index/ProgressBar/MangaIndexProgressBar';
import MangaIndexPosterSelect from 'Manga/Index/Select/MangaIndexPosterSelect';
import { Statistics } from 'Manga/Manga';
import { useMangaPosterOptions } from 'Manga/mangaOptionsStore';
import MangaPoster from 'Manga/MangaPoster';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
// Phase 17.3 D-14: formatDateTime/getRelativeDate imports dropped — the
// {nextAiring ? <div ...>} JSX block that consumed them was deleted (no
// airing concept in manga domain per DOMAIN-01/02). D-08 orphan-import sweep
// applied.
import translate from 'Utilities/String/translate';
import useMangaIndexItem from '../useMangaIndexItem';
import MangaIndexPosterInfo from './MangaIndexPosterInfo';
import styles from './MangaIndexPoster.css';

interface MangaIndexPosterProps {
  mangaId: number;
  sortKey: string;
  isSelectMode: boolean;
  posterWidth: number;
  posterHeight: number;
}

function MangaIndexPoster(props: MangaIndexPosterProps) {
  const { mangaId, sortKey, isSelectMode, posterWidth, posterHeight } = props;

  const { manga, qualityProfile, isRefreshingManga, isSearchingManga } =
    useMangaIndexItem(mangaId);

  const {
    detailedProgressBar,
    showTitle,
    showMonitored,
    showQualityProfile,
    showTags,
    showSearchAction,
  } = useMangaPosterOptions();

  const { showRelativeDates, shortDateFormat, longDateFormat, timeFormat } =
    useUiSettingsValues();

  const executeCommand = useExecuteCommand();
  const [hasPosterError, setHasPosterError] = useState(false);
  const [isEditMangaModalOpen, setIsEditMangaModalOpen] = useState(false);

  const onRefreshPress = useCallback(() => {
    executeCommand({
      name: CommandNames.RefreshManga,
      mangaIds: [mangaId],
    });
  }, [mangaId, executeCommand]);

  const onSearchPress = useCallback(() => {
    executeCommand({
      name: CommandNames.MangaSearch,
      mangaId,
    });
  }, [mangaId, executeCommand]);

  const onPosterLoadError = useCallback(() => {
    setHasPosterError(true);
  }, [setHasPosterError]);

  const onPosterLoad = useCallback(() => {
    setHasPosterError(false);
  }, [setHasPosterError]);

  const onEditMangaPress = useCallback(() => {
    setIsEditMangaModalOpen(true);
  }, [setIsEditMangaModalOpen]);

  const onEditMangaModalClose = useCallback(() => {
    setIsEditMangaModalOpen(false);
  }, [setIsEditMangaModalOpen]);

  if (!manga) {
    return null;
  }

  const {
    title,
    monitored,
    status,
    path,
    titleSlug,
    added,
    statistics = {} as Statistics,
    images,
    ratings,
    tags,
  } = manga;

  // Phase 17.3 D-14: originalCountry/originalLanguage/network/nextAiring/
  // previousAiring dropped from Manga destructure (no longer on Manga
  // interface per Phase 17.3 D-13). seasonCount/episodeCount/episodeFileCount/
  // totalEpisodeCount dropped from Statistics — replaced by chapter-shape
  // progress numbers. MangaIndexProgressBar still accepts the legacy
  // episode-named props as a facade (its body internally aliases them to
  // chapter-shape — see MangaIndexProgressBar.tsx L88-90), so we map the
  // chapter-shape fields here for verbatim-inheritance prop compatibility.
  const {
    chapterCount = 0,
    chapterFileCount = 0,
    totalChapterCount = 0,
    sizeOnDisk = 0,
  } = statistics;

  const link = `/manga/${titleSlug}`;

  const elementStyle = {
    width: `${posterWidth}px`,
    height: `${posterHeight}px`,
  };

  return (
    <div className={styles.content}>
      <div className={styles.posterContainer} title={title}>
        {isSelectMode ? (
          <MangaIndexPosterSelect mangaId={mangaId} titleSlug={titleSlug} />
        ) : null}

        <Label className={styles.controls}>
          <SpinnerIconButton
            className={styles.action}
            name={icons.REFRESH}
            title={translate('RefreshManga')}
            isSpinning={isRefreshingManga}
            tabIndex={-1}
            onPress={onRefreshPress}
          />

          {showSearchAction ? (
            <SpinnerIconButton
              className={styles.action}
              name={icons.SEARCH}
              title={translate('SearchForMonitoredChapters')}
              isSpinning={isSearchingManga}
              tabIndex={-1}
              onPress={onSearchPress}
            />
          ) : null}

          <IconButton
            className={styles.action}
            name={icons.EDIT}
            title={translate('EditManga')}
            aria-label={translate('EditManga')}
            tabIndex={-1}
            onPress={onEditMangaPress}
          />
        </Label>

        {/* Manga divergence: 'completed' fills the Mangarr 'ended' overlay slot;
            'cancelled' fills the 'deleted' slot. CSS class names preserved. */}
        {status === 'completed' ? (
          <div
            className={classNames(styles.status, styles.ended)}
            title={translate('Completed')}
          />
        ) : null}

        {status === 'cancelled' ? (
          <div
            className={classNames(styles.status, styles.deleted)}
            title={translate('Cancelled')}
          />
        ) : null}

        <Link className={styles.link} style={elementStyle} to={link}>
          <MangaPoster
            style={elementStyle}
            images={images}
            size={250}
            lazy={false}
            overflow={true}
            title={title}
            onError={onPosterLoadError}
            onLoad={onPosterLoad}
          />

          {hasPosterError ? (
            <div className={styles.overlayTitle}>{title}</div>
          ) : null}
        </Link>
      </div>

      <MangaIndexProgressBar
        mangaId={mangaId}
        monitored={monitored}
        status={status}
        episodeCount={chapterCount}
        episodeFileCount={chapterFileCount}
        totalEpisodeCount={totalChapterCount}
        width={posterWidth}
        detailedProgressBar={detailedProgressBar}
        isStandalone={false}
      />

      {showTitle ? (
        <div className={styles.title} title={title}>
          {title}
        </div>
      ) : null}

      {showMonitored ? (
        <div className={styles.title}>
          {monitored ? translate('Monitored') : translate('Unmonitored')}
        </div>
      ) : null}

      {showQualityProfile && !!qualityProfile?.name ? (
        <div className={styles.title} title={translate('QualityProfile')}>
          {qualityProfile.name}
        </div>
      ) : null}

      {/* Phase 17.3 D-14: top-of-render {nextAiring ? <div ...>} block dropped
          (no airing concept in manga domain; Manga.ts trim removed
          props.nextAiring). The .nextAiring CSS class was also dropped from
          MangaIndexPoster.css. */}

      {showTags && tags.length ? (
        <div className={styles.tags}>
          <div className={styles.tagsList}>
            <MangaTagList tags={tags} />
          </div>
        </div>
      ) : null}

      {/* Phase 17.3 D-14: originalCountry/originalLanguage/network/
          previousAiring/seasonCount props dropped from this call site (no
          longer on Manga interface per D-13; MangaIndexPosterInfo child also
          forked to drop the corresponding branches). */}
      <MangaIndexPosterInfo
        added={added}
        sizeOnDisk={sizeOnDisk}
        path={path}
        qualityProfile={qualityProfile}
        showQualityProfile={showQualityProfile}
        showRelativeDates={showRelativeDates}
        sortKey={sortKey}
        shortDateFormat={shortDateFormat}
        longDateFormat={longDateFormat}
        timeFormat={timeFormat}
        tags={tags}
        showTags={showTags}
        ratings={ratings}
      />

      <EditMangaModal
        isOpen={isEditMangaModalOpen}
        mangaId={mangaId}
        onModalClose={onEditMangaModalClose}
      />
    </div>
  );
}

export default MangaIndexPoster;
