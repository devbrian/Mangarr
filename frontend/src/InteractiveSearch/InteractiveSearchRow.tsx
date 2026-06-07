import React, { useCallback, useMemo, useState } from 'react';
import ProtocolLabel from 'Activity/Queue/ProtocolLabel';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import Popover from 'Components/Tooltip/Popover';
// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// Episode/EpisodeFormats + Episode/IndexerFlags no-op stub imports DROPPED
// (both Phase 15 Plan 15-12 STUB components return null with zero render
// output; the JSX render sites below also dropped). Manga indexers do not
// emit customFormats or indexerFlags in the v1 ChapterRelease shape. The
// `Tooltip` component (Components/Tooltip/Tooltip) became unused once its
// EpisodeFormats body was dropped — import removed to satisfy TS6133.
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import formatDateTime from 'Utilities/Date/formatDateTime';
import formatAge from 'Utilities/Number/formatAge';
import formatCustomFormatScore from 'Utilities/Number/formatCustomFormatScore';
import translate from 'Utilities/String/translate';
import InteractiveSearchPayload from './InteractiveSearchPayload';
// Payload-shape discriminator narrows on `searchPayload.kind === '…'` literal
// (Pitfall 2 grep gate). The union is manga-only ('chapter' / 'manga'); each
// routes to its manga-shape OverrideMatch sibling.
import ChapterOverrideMatchModal from './OverrideMatch/Chapter/ChapterOverrideMatchModal';
import MangaOverrideMatchModal from './OverrideMatch/Manga/MangaOverrideMatchModal';
import ReleaseSceneIndicator from './ReleaseSceneIndicator';
import { Release, useGrabMangaRelease } from './useReleases';
import styles from './InteractiveSearchRow.css';

function getDownloadIcon(
  isGrabbing: boolean,
  isGrabbed: boolean,
  grabError?: string
) {
  if (isGrabbing) {
    return icons.SPINNER;
  } else if (isGrabbed) {
    return icons.DOWNLOADING;
  } else if (grabError) {
    return icons.DOWNLOADING;
  }

  return icons.DOWNLOAD;
}

function getDownloadKind(isGrabbed: boolean, grabError?: string) {
  if (isGrabbed) {
    return kinds.SUCCESS;
  }

  if (grabError) {
    return kinds.DANGER;
  }

  return kinds.DEFAULT;
}

function getDownloadTooltip(
  isGrabbing: boolean,
  isGrabbed: boolean,
  grabError?: string
) {
  if (isGrabbing) {
    return '';
  } else if (isGrabbed) {
    return translate('AddedToDownloadQueue');
  } else if (grabError) {
    return grabError;
  }

  return translate('AddToDownloadQueue');
}

interface InteractiveSearchRowProps extends Release {
  searchPayload: InteractiveSearchPayload;
}

function InteractiveSearchRow(props: InteractiveSearchRowProps) {
  const {
    decision,
    history,
    parsedInfo,
    release,
    publishDate,
    customFormatScore,
    customFormats,
    sceneMapping,
    mappedSeasonNumber,
    mappedEpisodeNumbers,
    mappedAbsoluteEpisodeNumbers,
    indexerFlags = 0,
    episodeRequested,
    downloadAllowed,
    searchPayload,
    translatedLanguage,
    scanlationGroup,
  } = props;

  const { rejections = [] } = decision;

  const { absoluteEpisodeNumbers, episodeNumbers, isDaily, seasonNumber } =
    parsedInfo;

  const {
    guid,
    indexerId,
    age,
    ageHours,
    ageMinutes,
    title,
    infoUrl,
    indexer,
    source,
    votes,
    protocol,
  } = release;

  const { longDateFormat, timeFormat, timeZone } = useUiSettingsValues();

  const [isConfirmGrabModalOpen, setIsConfirmGrabModalOpen] = useState(false);
  const [isOverrideModalOpen, setIsOverrideModalOpen] = useState(false);

  // Bug fix (interactive-download-btn-red, 2026-05-08): the row-level
  // direct-grab routes to the same endpoint that produced the release list.
  // The InteractiveSearch union is manga-only ('chapter' / 'manga'), so the
  // grab always POSTs to /api/v5/manga/release via useGrabMangaRelease.
  const { isGrabbing, isGrabbed, grabError, grabRelease } =
    useGrabMangaRelease();

  const isBlocklisted = useMemo(() => {
    return (
      decision.rejections.findIndex((r) => r.reason === 'blocklisted') >= 0
    );
  }, [decision]);

  const handleGrabPress = useCallback(() => {
    if (downloadAllowed) {
      grabRelease({
        guid,
        indexerId,
      });

      return;
    }

    setIsConfirmGrabModalOpen(true);
  }, [
    guid,
    indexerId,
    downloadAllowed,
    grabRelease,
    setIsConfirmGrabModalOpen,
  ]);

  const onGrabConfirm = useCallback(() => {
    setIsConfirmGrabModalOpen(false);

    grabRelease({
      guid,
      indexerId,
      searchInfo: searchPayload,
    });
  }, [guid, indexerId, searchPayload, grabRelease, setIsConfirmGrabModalOpen]);

  const onGrabCancel = useCallback(() => {
    setIsConfirmGrabModalOpen(false);
  }, [setIsConfirmGrabModalOpen]);

  const onOverridePress = useCallback(() => {
    setIsOverrideModalOpen(true);
  }, [setIsOverrideModalOpen]);

  const onOverrideModalClose = useCallback(() => {
    setIsOverrideModalOpen(false);
  }, [setIsOverrideModalOpen]);

  // Phase 18 Plan-08 D-18 -- per-row testid keyed by release guid lets
  // PageObjects target individual rows. The rejected-icon span renders ONLY
  // when rejections.length > 0; per feedback_verify_ui_state_not_just_rendering
  // memory, this is the canonical state-not-rendering target.
  //
  // Testid shapes emitted at runtime (literal patterns for source-grep audits):
  //   interactive-search-row-{guid}
  //   interactive-search-row-{guid}-title
  //   interactive-search-row-{guid}-decision
  //   interactive-search-row-{guid}-rejected-icon
  //   interactive-search-row-{guid}-grab-button
  const rowTestId = `interactive-search-row-${guid}`;

  // Extracted from the JSX return to satisfy ESLint no-nested-ternary.
  // searchPayload.kind narrows the two manga-shape modal flavors; see the
  // comment block at the JSX call site for the wire-format rationale
  // (mangaId=0 sentinel for chapter flavor).
  const renderOverrideMatchModal = () => {
    if (searchPayload.kind === 'chapter') {
      return (
        <ChapterOverrideMatchModal
          isOpen={isOverrideModalOpen}
          title={title}
          indexerId={indexerId}
          guid={guid}
          mangaId={0}
          chapterIds={[searchPayload.chapterId]}
          protocol={protocol}
          onModalClose={onOverrideModalClose}
        />
      );
    }
    return (
      <MangaOverrideMatchModal
        isOpen={isOverrideModalOpen}
        title={title}
        indexerId={indexerId}
        guid={guid}
        mangaId={searchPayload.mangaId}
        protocol={protocol}
        onModalClose={onOverrideModalClose}
      />
    );
  };

  return (
    // Phase 33 D-11: data-source attribute exposes the originating
    // IndexerDefinition.Name so InteractiveSearch fixtures can assert
    // mixed-source rows render. Consumer: Plan 33-04 (3 InteractiveSearch
    // fixtures). Do not strip — Plan 33-04's assertion depends on it.
    <TableRow data-testid={rowTestId} data-source={indexer}>
      <TableRowCell className={styles.protocol}>
        <ProtocolLabel protocol={protocol} />
      </TableRowCell>

      <TableRowCell
        className={styles.age}
        title={formatDateTime(publishDate, longDateFormat, timeFormat, {
          includeSeconds: true,
          timeZone,
        })}
      >
        {formatAge(age, ageHours, ageMinutes)}
      </TableRowCell>

      <TableRowCell data-testid={`${rowTestId}-title`}>
        <div className={styles.titleContent}>
          <Link to={infoUrl}>{title}</Link>
          <ReleaseSceneIndicator
            className={styles.sceneMapping}
            seasonNumber={mappedSeasonNumber}
            episodeNumbers={mappedEpisodeNumbers}
            absoluteEpisodeNumbers={mappedAbsoluteEpisodeNumbers}
            sceneSeasonNumber={seasonNumber}
            sceneEpisodeNumbers={episodeNumbers}
            sceneAbsoluteEpisodeNumbers={absoluteEpisodeNumbers}
            sceneMapping={sceneMapping}
            episodeRequested={episodeRequested}
            isDaily={isDaily}
          />
        </div>
      </TableRowCell>

      <TableRowCell className={styles.indexer}>
        {indexer}
        {source ? (
          <div className={styles.source} aria-label={`Source: ${source}`}>
            {source}
          </div>
        ) : null}
      </TableRowCell>

      <TableRowCell className={styles.history}>
        {history ? (
          <Icon
            name={icons.DOWNLOADING}
            kind={history.failed ? kinds.DANGER : kinds.DEFAULT}
            title={`${
              history.failed
                ? translate('FailedAt', {
                    date: formatDateTime(
                      history.failed,
                      longDateFormat,
                      timeFormat,
                      { includeSeconds: true }
                    ),
                  })
                : translate('GrabbedAt', {
                    date: formatDateTime(
                      history.grabbed,
                      longDateFormat,
                      timeFormat,
                      { includeSeconds: true }
                    ),
                  })
            }`}
          />
        ) : null}

        {isBlocklisted ? (
          <Icon
            containerClassName={
              history ? styles.blocklistIconContainer : undefined
            }
            name={icons.BLOCKLIST}
            kind={kinds.DANGER}
            title={
              history?.failed
                ? `${translate('BlocklistedAt', {
                    date: formatDateTime(
                      history.failed,
                      longDateFormat,
                      timeFormat,
                      { includeSeconds: true }
                    ),
                  })}`
                : translate('Blocklisted')
            }
          />
        ) : null}
      </TableRowCell>

      <TableRowCell>{translatedLanguage?.toUpperCase() ?? ''}</TableRowCell>

      <TableRowCell>{scanlationGroup ?? ''}</TableRowCell>

      <TableRowCell>{votes ?? 0}</TableRowCell>

      <TableRowCell className={styles.customFormatScore}>
        {/* Sonarr divergence: Phase 17.3 Plan 17.3-13b — EpisodeFormats no-op
            stub dropped (zero render output). Tooltip retained showing only
            the formatCustomFormatScore anchor; the empty-tooltip body was
            indistinguishable from no-tooltip in the prior render path. */}
        {formatCustomFormatScore(customFormatScore, customFormats.length)}
      </TableRowCell>

      <TableRowCell className={styles.indexerFlags}>
        {/* Sonarr divergence: Phase 17.3 Plan 17.3-13b — IndexerFlags no-op
            stub dropped (zero render output). Popover body would have been
            empty; the FLAG icon was retained because indexerFlags!==0 is still
            a meaningful signal; clicking gave no info anyway. */}
        {indexerFlags ? (
          <Icon name={icons.FLAG} title={translate('IndexerFlags')} />
        ) : null}
      </TableRowCell>

      <TableRowCell
        className={styles.rejected}
        data-testid={`${rowTestId}-decision`}
      >
        {rejections.length ? (
          <span data-testid={`${rowTestId}-rejected-icon`}>
            <Popover
              anchor={<Icon name={icons.DANGER} kind={kinds.DANGER} />}
              title={translate('ReleaseRejected')}
              body={
                <ul>
                  {rejections.map((rejection, index) => {
                    return <li key={index}>{rejection.message}</li>;
                  })}
                </ul>
              }
              position={tooltipPositions.LEFT}
            />
          </span>
        ) : null}
      </TableRowCell>

      <TableRowCell className={styles.download}>
        {/* Phase 18 Plan-08 -- grab-button testid wrapper. */}
        <span data-testid={`${rowTestId}-grab-button`}>
          <SpinnerIconButton
            name={getDownloadIcon(isGrabbing, isGrabbed, grabError)}
            kind={getDownloadKind(isGrabbed, grabError)}
            title={getDownloadTooltip(isGrabbing, isGrabbed, grabError)}
            isSpinning={isGrabbing}
            onPress={handleGrabPress}
          />
        </span>

        <Link
          className={styles.manualDownloadContent}
          data-testid="interactive-search-row-override-trigger"
          title={translate('OverrideAndAddToDownloadQueue')}
          onPress={onOverridePress}
        >
          <div className={styles.manualDownloadContent}>
            <Icon
              className={styles.interactiveIcon}
              name={icons.INTERACTIVE}
              size={12}
            />

            <Icon
              className={styles.downloadIcon}
              name={icons.CIRCLE_DOWN}
              size={10}
            />
          </div>
        </Link>
      </TableRowCell>

      <ConfirmModal
        isOpen={isConfirmGrabModalOpen}
        kind={kinds.WARNING}
        title={translate('GrabRelease')}
        message={translate('GrabReleaseUnknownMangaOrChapterMessageText', {
          title,
        })}
        confirmLabel={translate('Grab')}
        onConfirm={onGrabConfirm}
        onCancel={onGrabCancel}
      />

      {/* searchPayload narrows on the `kind` literal-string discriminator
          (Pitfall 2 — no runtime property-presence checks). Two branches:
            * kind='chapter' → ChapterOverrideMatchModal (chapterIds REQUIRED
              non-empty; mangaId=0 WIRE SENTINEL)
            * kind='manga'   → MangaOverrideMatchModal (mangaId REQUIRED;
              no chapterIds)
          The WIRE-format sentinel `mangaId=0` is preserved for the chapter
          flavor — see ChapterOverrideMatchModal.tsx header + 25-REVIEW.md
          §WR-04 for the full disposition. The backend MangaReleaseController
          accepts mangaId=0 as the documented chapter-flavored override shape. */}
      {renderOverrideMatchModal()}
    </TableRow>
  );
}

export default InteractiveSearchRow;
