import classNames from 'classnames';
import React, { useCallback, useMemo, useState } from 'react';
import TextTruncate from 'react-text-truncate';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import MangaTagList from 'Components/MangaTagList';
import { icons } from 'Helpers/Props';
// fix(home-card-edit-button-no-op): swap stub `Series/Edit/EditSeriesModal`
// (Phase 15 Plan 15-12 `() => null`) for the real per-manga Edit modal
// shipped in PR #27. Sonarr-consistency-audit: mirrors the canonical
// `SeriesIndexOverview.js` modal-mount shape modulo `seriesId` → `mangaId`.
// The Overview row has no Delete button (only Refresh + optional Search +
// Edit), so the `DeleteSeriesModal` mount and `onDeleteSeriesPress` chain
// are dropped — they were dead under the stub and would still be dead now.
import EditMangaModal from 'Manga/Edit/EditMangaModal';
import MangaIndexProgressBar from 'Manga/Index/ProgressBar/MangaIndexProgressBar';
import MangaIndexPosterSelect from 'Manga/Index/Select/MangaIndexPosterSelect';
import { Statistics } from 'Manga/Manga';
import { useMangaOverviewOptions } from 'Manga/mangaOptionsStore';
import MangaPoster from 'Manga/MangaPoster';
import dimensions from 'Styles/Variables/dimensions';
import fonts from 'Styles/Variables/fonts';
import translate from 'Utilities/String/translate';
import useMangaIndexItem from '../useMangaIndexItem';
import MangaIndexOverviewInfo from './MangaIndexOverviewInfo';
import styles from './MangaIndexOverview.css';

const columnPadding = parseInt(dimensions.seriesIndexColumnPadding);
const columnPaddingSmallScreen = parseInt(
  dimensions.seriesIndexColumnPaddingSmallScreen
);
const defaultFontSize = parseInt(fonts.defaultFontSize);
const lineHeight = parseFloat(fonts.lineHeight);

// Hardcoded height based on line-height of 32 + bottom margin of 10.
// Less side-effecty than using react-measure.
const TITLE_HEIGHT = 42;

interface MangaIndexOverviewProps {
  mangaId: number;
  sortKey: string;
  posterWidth: number;
  posterHeight: number;
  rowHeight: number;
  isSelectMode: boolean;
  isSmallScreen: boolean;
}

function MangaIndexOverview(props: MangaIndexOverviewProps) {
  const {
    mangaId,
    sortKey,
    posterWidth,
    posterHeight,
    rowHeight,
    isSelectMode,
    isSmallScreen,
  } = props;

  const { manga, qualityProfile, isRefreshingManga, isSearchingManga } =
    useMangaIndexItem(mangaId);

  const overviewOptions = useMangaOverviewOptions();

  const executeCommand = useExecuteCommand();
  const [isEditMangaModalOpen, setIsEditMangaModalOpen] = useState(false);

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
    // nothing to search"). The status reports as "completed" — misleading. Mirror
    // the Table view's MangaIndexRow.onSearchPress plural shape.
    executeCommand({
      name: CommandNames.MangaSearch,
      mangaIds: [mangaId],
    });
  }, [mangaId, executeCommand]);

  const onEditMangaPress = useCallback(() => {
    setIsEditMangaModalOpen(true);
  }, [setIsEditMangaModalOpen]);

  const onEditMangaModalClose = useCallback(() => {
    setIsEditMangaModalOpen(false);
  }, [setIsEditMangaModalOpen]);

  const contentHeight = useMemo(() => {
    const padding = isSmallScreen ? columnPaddingSmallScreen : columnPadding;

    return rowHeight - padding;
  }, [rowHeight, isSmallScreen]);

  const overviewHeight = contentHeight - TITLE_HEIGHT;

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
    overview,
    statistics = {} as Statistics,
    images,
    tags,
  } = manga;

  // Phase 17.3 D-14: nextAiring/previousAiring/network dropped from Manga
  // (no airing concept; no network in manga domain). seasonCount/episodeCount/
  // episodeFileCount/totalEpisodeCount dropped from Statistics — replaced by
  // chapter-shape progress numbers. MangaIndexProgressBar still accepts the
  // legacy episode-named props as a facade (its body internally aliases them
  // to chapter-shape — see MangaIndexProgressBar.tsx L88-90), so we map the
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
    <div>
      <div className={styles.content}>
        <div className={styles.poster}>
          <div className={styles.posterContainer}>
            {isSelectMode ? (
              <MangaIndexPosterSelect mangaId={mangaId} titleSlug={titleSlug} />
            ) : null}

            {/* Manga divergence: 'completed' status fills the corner-overlay
                slot Mangarr uses for 'ended'; 'cancelled' fills the slot used
                for 'deleted'. CSS class names preserved for Phase 8 collapse. */}
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
                className={styles.poster}
                style={elementStyle}
                images={images}
                size={250}
                lazy={false}
                overflow={true}
                title={title}
              />
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
            detailedProgressBar={overviewOptions.detailedProgressBar}
            isStandalone={false}
          />
        </div>

        <div className={styles.info} style={{ maxHeight: contentHeight }}>
          <div className={styles.titleRow}>
            <Link className={styles.title} to={link}>
              {title}
            </Link>

            <div className={styles.actions}>
              <SpinnerIconButton
                name={icons.REFRESH}
                title={translate('RefreshManga')}
                isSpinning={isRefreshingManga}
                onPress={onRefreshPress}
              />

              {overviewOptions.showSearchAction ? (
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
            </div>
          </div>

          <div className={styles.details}>
            <div className={styles.overviewContainer}>
              <Link className={styles.overview} to={link}>
                <TextTruncate
                  line={Math.floor(
                    overviewHeight / (defaultFontSize * lineHeight)
                  )}
                  text={overview}
                />
              </Link>

              {overviewOptions.showTags ? (
                <div className={styles.tags}>
                  <MangaTagList tags={tags} />
                </div>
              ) : null}
            </div>
            <MangaIndexOverviewInfo
              height={overviewHeight}
              monitored={monitored}
              added={added}
              qualityProfile={qualityProfile}
              sizeOnDisk={sizeOnDisk}
              path={path}
              sortKey={sortKey}
              {...overviewOptions}
            />
          </div>
        </div>
      </div>

      <EditMangaModal
        isOpen={isEditMangaModalOpen}
        mangaId={mangaId}
        onModalClose={onEditMangaModalClose}
      />
    </div>
  );
}

export default MangaIndexOverview;
