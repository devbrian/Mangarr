import classNames from 'classnames';
import React, { useCallback, useState } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import CheckInput from 'Components/Form/CheckInput';
import HeartRating from 'Components/HeartRating';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import SeriesTagList from 'Components/SeriesTagList';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import VirtualTableSelectCell from 'Components/Table/Cells/VirtualTableSelectCell';
import Column from 'Components/Table/Column';
import getReleaseTypeName from 'Episode/getReleaseTypeName';
import { icons } from 'Helpers/Props';
import useCountryName from 'Internationalization/useCountryName';
import DeleteMangaModal from "Series/Delete/DeleteSeriesModal";
import EditMangaModal from "Series/Edit/EditSeriesModal";
import { Statistics } from 'Manga/Manga';
import MangaBanner from 'Manga/MangaBanner';
import { useMangaTableOptions } from 'Manga/mangaOptionsStore';
import MangaTitleLink from 'Manga/MangaTitleLink';
import { SelectStateInputProps } from 'typings/props';
import formatBytes from 'Utilities/Number/formatBytes';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import MangaIndexProgressBar from '../ProgressBar/MangaIndexProgressBar';
import useMangaIndexItem from '../useMangaIndexItem';
import hasGrowableColumns from './hasGrowableColumns';
// SeasonsCell omitted per Plan 07-04 Lock #10 (no seasons in manga). Cells
// referencing 'seasons' / 'latestSeason' are short-circuited to a hyphen below.
import MangaStatusCell from './MangaStatusCell';
import styles from './MangaIndexRow.css';

interface MangaIndexRowProps {
  mangaId: number;
  sortKey: string;
  columns: Column[];
  isSelectMode: boolean;
}

function MangaIndexRow(props: MangaIndexRowProps) {
  const { mangaId, columns, isSelectMode } = props;

  const {
    manga,
    qualityProfile,
    latestSeason,
    isRefreshingSeries,
    isSearchingSeries,
  } = useMangaIndexItem(mangaId);

  const { showBanners, showSearchAction } = useMangaTableOptions();

  const executeCommand = useExecuteCommand();
  const [hasBannerError, setHasBannerError] = useState(false);
  const [isEditMangaModalOpen, setIsEditMangaModalOpen] = useState(false);
  const [isDeleteMangaModalOpen, setIsDeleteMangaModalOpen] = useState(false);
  const { getIsSelected, toggleSelected } = useSelect();
  const originalCountryName = useCountryName(manga?.originalCountry);

  const onRefreshPress = useCallback(() => {
    executeCommand({
      name: CommandNames.RefreshSeries,
      seriesIds: [mangaId],
    });
  }, [mangaId, executeCommand]);

  const onSearchPress = useCallback(() => {
    executeCommand({
      name: CommandNames.SeriesSearch,
      seriesId: mangaId,
    });
  }, [mangaId, executeCommand]);

  const onBannerLoadError = useCallback(() => {
    setHasBannerError(true);
  }, [setHasBannerError]);

  const onBannerLoad = useCallback(() => {
    setHasBannerError(false);
  }, [setHasBannerError]);

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

  const checkInputCallback = useCallback(() => {
    // Mock handler to satisfy `onChange` being required for `CheckInput`.
  }, []);

  const onSelectedChange = useCallback(
    ({ id, value, shiftKey }: SelectStateInputProps) => {
      toggleSelected({
        id,
        isSelected: value,
        shiftKey,
      });
    },
    [toggleSelected]
  );

  if (!manga) {
    return null;
  }

  const {
    title,
    monitored,
    monitorNewItems,
    status,
    path,
    titleSlug,
    nextAiring,
    previousAiring,
    added,
    statistics = {} as Statistics,
    seasonFolder,
    images,
    seriesType,
    network,
    originalLanguage,
    certification,
    year,
    useSceneNumbering,
    genres = [],
    ratings,
    // seasons / seasonCount unreferenced after Plan 07-04 Lock #10 dropped
    // SeasonsCell + the related cells; kept on the destructure because the
    // verbatim port elsewhere may grow them back in a future plan.
    tags = [],
  } = manga;
  void manga.seasons;

  const {
    episodeCount = 0,
    episodeFileCount = 0,
    totalEpisodeCount = 0,
    sizeOnDisk = 0,
    releaseGroups = [],
    releaseTypes = [],
    episodeFileQualities = [],
  } = statistics;

  return (
    <>
      {isSelectMode ? (
        <VirtualTableSelectCell
          id={mangaId}
          isSelected={getIsSelected(mangaId)}
          isDisabled={false}
          onSelectedChange={onSelectedChange}
        />
      ) : null}

      {columns.map((column) => {
        const { name, isVisible } = column;

        if (!isVisible) {
          return null;
        }

        if (name === 'status') {
          return (
            <MangaStatusCell
              key={name}
              className={styles[name]}
              mangaId={mangaId}
              monitored={monitored}
              status={status}
              isSelectMode={isSelectMode}
              component={VirtualTableRowCell}
            />
          );
        }

        if (name === 'sortTitle') {
          return (
            <VirtualTableRowCell
              key={name}
              className={classNames(
                styles[name],
                showBanners && styles.banner,
                showBanners && !hasGrowableColumns(columns) && styles.bannerGrow
              )}
            >
              {showBanners ? (
                <Link className={styles.link} to={`/manga/${titleSlug}`}>
                  <MangaBanner
                    className={styles.bannerImage}
                    images={images}
                    lazy={false}
                    overflow={true}
                    title={title}
                    onError={onBannerLoadError}
                    onLoad={onBannerLoad}
                  />

                  {hasBannerError && (
                    <div className={styles.overlayTitle}>{title}</div>
                  )}
                </Link>
              ) : (
                <MangaTitleLink titleSlug={titleSlug} title={title} />
              )}
            </VirtualTableRowCell>
          );
        }

        if (name === 'seriesType') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {titleCase(seriesType)}
            </VirtualTableRowCell>
          );
        }

        if (name === 'network') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {network}
            </VirtualTableRowCell>
          );
        }

        if (name === 'originalCountry') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {originalCountryName}
            </VirtualTableRowCell>
          );
        }

        if (name === 'originalLanguage') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {originalLanguage?.name ?? ''}
            </VirtualTableRowCell>
          );
        }

        if (name === 'qualityProfileId') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {qualityProfile?.name ?? ''}
            </VirtualTableRowCell>
          );
        }

        if (name === 'nextAiring') {
          return (
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-ignore ts(2739)
            <RelativeDateCell
              key={name}
              className={styles[name]}
              date={nextAiring}
              component={VirtualTableRowCell}
            />
          );
        }

        if (name === 'previousAiring') {
          return (
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-ignore ts(2739)
            <RelativeDateCell
              key={name}
              className={styles[name]}
              date={previousAiring}
              component={VirtualTableRowCell}
            />
          );
        }

        if (name === 'added') {
          return (
            // eslint-disable-next-line @typescript-eslint/ban-ts-comment
            // @ts-ignore ts(2739)
            <RelativeDateCell
              key={name}
              className={styles[name]}
              date={added}
              component={VirtualTableRowCell}
            />
          );
        }

        if (name === 'seasonCount') {
          // Manga has no seasons (Plan 07-04 Lock #10). Render an em-dash; the
          // 'seasonCount' column is hidden by default in mangaOptionsStore but
          // a user with TV cookies could land it here so we keep the branch.
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {'—'}
            </VirtualTableRowCell>
          );
        }

        if (name === 'seasonFolder') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <CheckInput
                name="seasonFolder"
                value={seasonFolder}
                isDisabled={true}
                onChange={checkInputCallback}
              />
            </VirtualTableRowCell>
          );
        }

        if (name === 'episodeProgress') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <MangaIndexProgressBar mangaId={mangaId}
                monitored={monitored}
                status={status}
                episodeCount={episodeCount}
                episodeFileCount={episodeFileCount}
                totalEpisodeCount={totalEpisodeCount}
                width={125}
                detailedProgressBar={true}
                isStandalone={true}
              />
            </VirtualTableRowCell>
          );
        }

        if (name === 'latestSeason') {
          if (!latestSeason) {
            return <VirtualTableRowCell key={name} className={styles[name]} />;
          }

          const seasonStatistics = latestSeason.statistics || {};

          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <MangaIndexProgressBar mangaId={mangaId}
                seasonNumber={latestSeason.seasonNumber}
                monitored={monitored}
                status={status}
                episodeCount={seasonStatistics.episodeCount ?? 0}
                episodeFileCount={seasonStatistics.episodeFileCount ?? 0}
                totalEpisodeCount={seasonStatistics.totalEpisodeCount ?? 0}
                width={125}
                detailedProgressBar={true}
                isStandalone={true}
              />
            </VirtualTableRowCell>
          );
        }

        if (name === 'episodeCount') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {totalEpisodeCount}
            </VirtualTableRowCell>
          );
        }

        if (name === 'year') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {year}
            </VirtualTableRowCell>
          );
        }

        if (name === 'path') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {path}
            </VirtualTableRowCell>
          );
        }

        if (name === 'sizeOnDisk') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {formatBytes(sizeOnDisk)}
            </VirtualTableRowCell>
          );
        }

        if (name === 'averageSizePerEpisode') {
          const averageSize =
            totalEpisodeCount > 0 ? sizeOnDisk / totalEpisodeCount : 0;

          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {averageSize ? formatBytes(averageSize) : null}
            </VirtualTableRowCell>
          );
        }

        if (name === 'genres') {
          const joinedGenres = genres.join(', ');

          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <span title={joinedGenres}>{joinedGenres}</span>
            </VirtualTableRowCell>
          );
        }

        if (name === 'ratings') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {ratings ? (
                <HeartRating rating={ratings.value} votes={ratings.votes} />
              ) : null}
            </VirtualTableRowCell>
          );
        }

        if (name === 'certification') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {certification}
            </VirtualTableRowCell>
          );
        }

        if (name === 'releaseGroups') {
          const joinedReleaseGroups = releaseGroups.join(', ');
          const truncatedReleaseGroups =
            releaseGroups.length > 3
              ? `${releaseGroups.slice(0, 3).join(', ')}...`
              : joinedReleaseGroups;

          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <span title={joinedReleaseGroups}>{truncatedReleaseGroups}</span>
            </VirtualTableRowCell>
          );
        }

        if (name === 'releaseTypes') {
          const joinedReleaseTypes = releaseTypes
            .map(getReleaseTypeName)
            .join(', ');
          const truncatedReleaseTypes =
            releaseTypes.length > 3
              ? `${releaseTypes
                  .slice(0, 3)
                  .map(getReleaseTypeName)
                  .join(', ')}...`
              : joinedReleaseTypes;

          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <span title={joinedReleaseTypes}>{truncatedReleaseTypes}</span>
            </VirtualTableRowCell>
          );
        }

        if (name === 'episodeFileQualities') {
          const joinedQualities = episodeFileQualities
            .map((q) => q.name)
            .join(', ');
          const truncatedQualities =
            episodeFileQualities.length > 3
              ? `${episodeFileQualities
                  .slice(0, 3)
                  .map((q) => q.name)
                  .join(', ')}...`
              : joinedQualities;

          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <span title={joinedQualities}>{truncatedQualities}</span>
            </VirtualTableRowCell>
          );
        }

        if (name === 'tags') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <SeriesTagList tags={tags} />
            </VirtualTableRowCell>
          );
        }

        if (name === 'useSceneNumbering') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <CheckInput
                className={styles.checkInput}
                name="useSceneNumbering"
                value={useSceneNumbering}
                isDisabled={true}
                onChange={checkInputCallback}
              />
            </VirtualTableRowCell>
          );
        }

        if (name === 'monitorNewItems') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {monitorNewItems === 'all'
                ? translate('SeasonsMonitoredAll')
                : translate('SeasonsMonitoredNone')}
            </VirtualTableRowCell>
          );
        }

        if (name === 'actions') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <SpinnerIconButton
                name={icons.REFRESH}
                title={translate('RefreshSeries')}
                isSpinning={isRefreshingSeries}
                onPress={onRefreshPress}
              />

              {showSearchAction ? (
                <SpinnerIconButton
                  name={icons.SEARCH}
                  title={translate('SearchForMonitoredEpisodes')}
                  isSpinning={isSearchingSeries}
                  onPress={onSearchPress}
                />
              ) : null}

              <IconButton
                name={icons.EDIT}
                title={translate('EditManga')}
                aria-label={translate('EditManga')}
                onPress={onEditMangaPress}
              />
            </VirtualTableRowCell>
          );
        }

        return null;
      })}

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
    </>
  );
}

export default MangaIndexRow;
