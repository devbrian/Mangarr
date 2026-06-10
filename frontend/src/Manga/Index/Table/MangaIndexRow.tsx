import classNames from 'classnames';
import React, { useCallback, useState } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import HeartRating from 'Components/HeartRating';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import MangaTagList from 'Components/MangaTagList';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import VirtualTableSelectCell from 'Components/Table/Cells/VirtualTableSelectCell';
import Column from 'Components/Table/Column';
import { icons } from 'Helpers/Props';
import ReleaseType from 'InteractiveImport/ReleaseType';
// fix(home-card-edit-button-no-op): replaced the Phase 15 Plan 15-12
// `null : null` per-row Edit modal stub with the real per-manga
// `Manga/Edit/EditMangaModal` shipped in PR #27. Sonarr-consistency-audit:
// mirrors canonical `SeriesIndexRow.js` modal-mount shape modulo
// `seriesId` → `mangaId`. The per-row action cell has no Delete button
// (only Refresh + optional Search + Edit), so the `DeleteSeriesModal`
// mount and `onDeleteSeriesPress` chain are not wired here — they were
// dead under the stub and remain out-of-scope for this fix-forward PR.
import EditMangaModal from 'Manga/Edit/EditMangaModal';
import MangaIndexProgressBar from 'Manga/Index/ProgressBar/MangaIndexProgressBar';
import Manga, { Statistics } from 'Manga/Manga';
import MangaBanner from 'Manga/MangaBanner';
import { useMangaTableOptions } from 'Manga/mangaOptionsStore';
import MangaTitleLink from 'Manga/MangaTitleLink';
import { useCustomFormatProfilesData } from 'Settings/Profiles/CustomFormatProfile/useCustomFormatProfiles';
import { useTranslationProfileName } from 'Settings/Profiles/Translations/TranslationProfileName';
import { SelectStateInputProps } from 'typings/props';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import useMangaIndexItem from '../useMangaIndexItem';
import hasGrowableColumns from './hasGrowableColumns';
// Phase 17.3 D-14 (2026-05-11): Table view forked to drop TV-shape carry-over
// reads (network / originalCountry / originalLanguage / nextAiring /
// previousAiring / seasonFolder / seasons / seriesType / useSceneNumbering)
// and the season/episode-count columns (latestSeason / episodeCount /
// episodeProgress / averageSizePerEpisode / episodeFileQualities) — historical
// Plan 07-04 Lock #10 verbatim-inheritance carry-over now unwound per the
// "v1 ships free of TV vocabulary" lock. Chapter-shape progress wiring deferred
// to a future plan; `chapterProgress` column in mangaOptionsStore is currently
// inert at the row level until wired.
import MangaStatusCell from './MangaStatusCell';
import styles from './MangaIndexRow.css';

function getReleaseTypeName(releaseType?: ReleaseType): string | null {
  switch (releaseType) {
    case 'singleEpisode':
      return translate('SingleChapter');
    case 'multiEpisode':
      return translate('MultiChapter');
    // Sonarr divergence (GH #327): 'seasonPack' case dropped — the backend ReleaseType
    // enum value is never produced for manga; falls through to Unknown.
    default:
      return translate('Unknown');
  }
}

// quick-260610-im4: derive a metadata-source label from whichever cross-source
// id the manga carries (MangaResource does not emit a dedicated source field).
function getMetadataSourceName(manga: Manga): string {
  if (manga.mangaBakaId) {
    return 'MangaBaka';
  }
  if (manga.mangaDexId) {
    return 'MangaDex';
  }
  if (manga.aniListId) {
    return 'AniList';
  }
  if (manga.malId) {
    return 'MyAnimeList';
  }
  return '';
}

interface MangaIndexRowProps {
  mangaId: number;
  sortKey: string;
  columns: Column[];
  isSelectMode: boolean;
}

function MangaIndexRow(props: MangaIndexRowProps) {
  const { mangaId, columns, isSelectMode } = props;

  const { manga, qualityProfile, isRefreshingManga, isSearchingManga } =
    useMangaIndexItem(mangaId);

  const { showBanners, showSearchAction } = useMangaTableOptions();

  // quick-260610-im4: profile-name resolvers for the translationProfileId /
  // customFormatProfileId table columns (previously these visible columns fell
  // through to a `null` cell — blank + width-misaligned). Hooks run
  // unconditionally before the `if (!manga)` short-circuit.
  const translationProfileName = useTranslationProfileName(
    manga?.translationProfileId
  );
  const customFormatProfiles = useCustomFormatProfilesData();

  const executeCommand = useExecuteCommand();
  const [hasBannerError, setHasBannerError] = useState(false);
  const [isEditMangaModalOpen, setIsEditMangaModalOpen] = useState(false);
  const { getIsSelected, toggleSelected } = useSelect();

  const onRefreshPress = useCallback(() => {
    executeCommand({
      name: CommandNames.RefreshManga,
      mangaIds: [mangaId],
    });
  }, [mangaId, executeCommand]);

  const onSearchPress = useCallback(() => {
    // MangaSearchCommand binds to `MangaIds: List<int>` (plural) per Phase 6 D-06
    // bulk-dispatch shape; a singular `mangaId` payload silently no-ops in
    // MangaSearchService.Execute ("MangaSearchCommand received with no MangaIds;
    // nothing to search"). The status reports as "completed" — misleading.
    executeCommand({
      name: CommandNames.MangaSearch,
      mangaIds: [mangaId],
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
    status,
    path,
    titleSlug,
    added,
    statistics = {} as Statistics,
    images,
    certification,
    contentRating,
    customFormatProfileId,
    totalChapterCount,
    year,
    genres = [],
    ratings,
    tags = [],
  } = manga;

  const {
    sizeOnDisk = 0,
    releaseGroups = [],
    releaseTypes = [],
    chapterCount = 0,
    chapterFileCount = 0,
    totalChapterCount: statisticsTotalChapterCount = 0,
  } = statistics;

  // quick-260610-im4: prefer the top-level manga count, fall back to the
  // statistics aggregate (either may be populated depending on refresh state).
  const displayChapterCount =
    totalChapterCount ?? statisticsTotalChapterCount ?? chapterCount;

  const customFormatProfileName =
    customFormatProfileId == null
      ? ''
      : customFormatProfiles.find((p) => p.id === customFormatProfileId)
          ?.name ?? '';

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

        if (name === 'qualityProfileId') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {qualityProfile?.name ?? ''}
            </VirtualTableRowCell>
          );
        }

        // quick-260610-im4: manga-canonical columns registered in
        // mangaOptionsStore.ts that previously had no row branch (fell through
        // to `return null` → blank cell + width misalignment).
        if (name === 'translationProfileId') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {translationProfileName ?? ''}
            </VirtualTableRowCell>
          );
        }

        if (name === 'customFormatProfileId') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {customFormatProfileName}
            </VirtualTableRowCell>
          );
        }

        if (name === 'chapterProgress') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <MangaIndexProgressBar
                mangaId={mangaId}
                monitored={monitored}
                status={status}
                episodeCount={chapterCount}
                episodeFileCount={chapterFileCount}
                totalEpisodeCount={statisticsTotalChapterCount}
                width={125}
                detailedProgressBar={true}
                isStandalone={true}
              />
            </VirtualTableRowCell>
          );
        }

        if (name === 'chapterCount') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {displayChapterCount}
            </VirtualTableRowCell>
          );
        }

        if (name === 'metadataSource') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {getMetadataSourceName(manga)}
            </VirtualTableRowCell>
          );
        }

        if (name === 'contentRating') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              {contentRating}
            </VirtualTableRowCell>
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

        if (name === 'tags') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <MangaTagList tags={tags} />
            </VirtualTableRowCell>
          );
        }

        if (name === 'actions') {
          return (
            <VirtualTableRowCell key={name} className={styles[name]}>
              <SpinnerIconButton
                name={icons.REFRESH}
                title={translate('RefreshManga')}
                isSpinning={isRefreshingManga}
                onPress={onRefreshPress}
              />

              {showSearchAction ? (
                <SpinnerIconButton
                  name={icons.SEARCH}
                  title={translate('SearchForMonitoredChapters')}
                  isSpinning={isSearchingManga}
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

        // quick-260610-im4: catch-all for any other VISIBLE column that has no
        // explicit branch (e.g. scanlationGroups / translatedLanguages /
        // originalCountry / originalLanguage — registered in mangaOptionsStore
        // but not yet backed by a MangaResource field). Render an empty but
        // width-correct cell instead of `null` so the body row keeps the same
        // cell count as the header and the table never squishes/misaligns.
        return (
          <VirtualTableRowCell
            key={name}
            className={styles[name as keyof typeof styles]}
          />
        );
      })}

      <EditMangaModal
        isOpen={isEditMangaModalOpen}
        mangaId={mangaId}
        onModalClose={onEditMangaModalClose}
      />
    </>
  );
}

export default MangaIndexRow;
