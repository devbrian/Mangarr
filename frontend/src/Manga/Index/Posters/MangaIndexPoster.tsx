import classNames from 'classnames';
import React, { useCallback, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import SeriesTagList from 'Components/SeriesTagList';
import { icons } from 'Helpers/Props';
import DeleteMangaModal from "Series/Delete/DeleteSeriesModal";
import EditMangaModal from "Series/Edit/EditSeriesModal";
import MangaIndexProgressBar from 'Manga/Index/ProgressBar/MangaIndexProgressBar';
import MangaIndexPosterSelect from 'Manga/Index/Select/MangaIndexPosterSelect';
import { Statistics } from 'Manga/Manga';
import { useMangaPosterOptions } from 'Manga/mangaOptionsStore';
import MangaPoster from 'Manga/MangaPoster';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import formatDateTime from 'Utilities/Date/formatDateTime';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
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
  const [isDeleteMangaModalOpen, setIsDeleteMangaModalOpen] = useState(false);

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

  const onDeleteMangaPress = useCallback(() => {
    setIsEditMangaModalOpen(false);
    setIsDeleteMangaModalOpen(true);
  }, [setIsDeleteMangaModalOpen]);

  const onDeleteMangaModalClose = useCallback(() => {
    setIsDeleteMangaModalOpen(false);
  }, [setIsDeleteMangaModalOpen]);

  if (!manga) {
    return null;
  }

  const {
    title,
    monitored,
    status,
    path,
    titleSlug,
    originalCountry,
    originalLanguage,
    network,
    nextAiring,
    previousAiring,
    added,
    statistics = {} as Statistics,
    images,
    ratings,
    tags,
  } = manga;

  const {
    seasonCount = 0,
    episodeCount = 0,
    episodeFileCount = 0,
    totalEpisodeCount = 0,
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

      <MangaIndexProgressBar mangaId={mangaId}
        monitored={monitored}
        status={status}
        episodeCount={episodeCount}
        episodeFileCount={episodeFileCount}
        totalEpisodeCount={totalEpisodeCount}
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

      {nextAiring ? (
        <div
          className={styles.nextAiring}
          title={`${translate('NextAiring')}: ${formatDateTime(
            nextAiring,
            longDateFormat,
            timeFormat
          )}`}
        >
          {getRelativeDate({
            date: nextAiring,
            shortDateFormat,
            showRelativeDates,
            timeFormat,
            timeForToday: true,
          })}
        </div>
      ) : null}

      {showTags && tags.length ? (
        <div className={styles.tags}>
          <div className={styles.tagsList}>
            <SeriesTagList tags={tags} />
          </div>
        </div>
      ) : null}

      <MangaIndexPosterInfo
        originalCountry={originalCountry}
        originalLanguage={originalLanguage}
        network={network}
        previousAiring={previousAiring}
        added={added}
        seasonCount={seasonCount}
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
        seriesId={mangaId}
        onModalClose={onEditMangaModalClose}
        onDeleteSeriesPress={onDeleteMangaPress}
      />

      <DeleteMangaModal
        isOpen={isDeleteMangaModalOpen}
        seriesId={mangaId}
        onModalClose={onDeleteMangaModalClose}
      />
    </div>
  );
}

export default MangaIndexPoster;
