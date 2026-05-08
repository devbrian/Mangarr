import React, { useCallback, useMemo, useState } from 'react';
import ProtocolLabel from 'Activity/Queue/ProtocolLabel';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import Popover from 'Components/Tooltip/Popover';
import Tooltip from 'Components/Tooltip/Tooltip';
import EpisodeFormats from 'Episode/EpisodeFormats';
import EpisodeLanguages from 'Episode/EpisodeLanguages';
import EpisodeQuality from 'Episode/EpisodeQuality';
import IndexerFlags from 'Episode/IndexerFlags';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import formatDateTime from 'Utilities/Date/formatDateTime';
import formatAge from 'Utilities/Number/formatAge';
import formatBytes from 'Utilities/Number/formatBytes';
import formatCustomFormatScore from 'Utilities/Number/formatCustomFormatScore';
import translate from 'Utilities/String/translate';
import InteractiveSearchPayload from './InteractiveSearchPayload';
// Phase 12 Plan 12-10 — Sub-wave-B-addition (audit row C closure):
// MangaOverrideMatchModal is the manga-shape sibling for chapter/manga payloads.
// Routed at the OverrideMatchModal mount point below via payload-shape discriminator
// `'chapterId' in searchPayload || 'mangaId' in searchPayload` — verbatim shape from
// useReleases.ts:385 (Plan 07-05 union-routing pattern). TV branch preserved verbatim
// per D-12-18.
import MangaOverrideMatchModal from './OverrideMatch/Manga/MangaOverrideMatchModal';
import OverrideMatchModal from './OverrideMatch/OverrideMatchModal';
import Peers from './Peers';
import ReleaseSceneIndicator from './ReleaseSceneIndicator';
import { Release, useGrabMangaRelease, useGrabRelease } from './useReleases';
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
    languages,
    customFormatScore,
    customFormats,
    sceneMapping,
    mappedSeriesId,
    mappedSeasonNumber,
    mappedEpisodeNumbers,
    mappedAbsoluteEpisodeNumbers,
    mappedEpisodeInfo,
    indexerFlags = 0,
    episodeRequested,
    downloadAllowed,
    searchPayload,
  } = props;

  const { rejections = [] } = decision;

  const {
    absoluteEpisodeNumbers,
    episodeNumbers,
    isDaily,
    seasonNumber,
    quality,
  } = parsedInfo;

  const {
    guid,
    indexerId,
    age,
    ageHours,
    ageMinutes,
    title,
    infoUrl,
    indexer,
    size,
    seeders,
    leechers,
    protocol,
  } = release;

  const { longDateFormat, timeFormat, timeZone } = useUiSettingsValues();

  const [isConfirmGrabModalOpen, setIsConfirmGrabModalOpen] = useState(false);
  const [isOverrideModalOpen, setIsOverrideModalOpen] = useState(false);

  // Bug fix (interactive-download-btn-red, 2026-05-08):
  // The row-level direct-grab must route to the same endpoint that produced the
  // release list. Phase 12 Plan 12-10 fixed the OverrideMatch modal to do this
  // (see OverrideMatch routing at the bottom of this file) but the row-level
  // download icon was still hard-wired to useGrabRelease (POSTs to /api/v5/release,
  // which does not exist in this codebase — the TV ReleaseController was deleted
  // during the Phase 6/8 manga cutover). Result: every chapter grab 404'd and the
  // icon turned red.
  //
  // Both hooks expose an identical surface ({ grabRelease, isGrabbing, isGrabbed,
  // grabError }); the rules of hooks require us to call both unconditionally and
  // pick the active pair by payload shape. The discriminator
  // `'chapterId' in searchPayload || 'mangaId' in searchPayload` matches the same
  // shape check used at the OverrideMatchModal mount below and at
  // useReleases.ts:385's getReleasePath. Phase 15 cleanup will collapse the two
  // hooks into one when Tv/ deletes.
  const isMangaPayload =
    'chapterId' in searchPayload || 'mangaId' in searchPayload;
  const tvGrab = useGrabRelease();
  const mangaGrab = useGrabMangaRelease();
  const { isGrabbing, isGrabbed, grabError, grabRelease } = isMangaPayload
    ? mangaGrab
    : tvGrab;

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

  return (
    <TableRow>
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

      <TableRowCell>
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

      <TableRowCell className={styles.indexer}>{indexer}</TableRowCell>

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

      <TableRowCell className={styles.size}>{formatBytes(size)}</TableRowCell>

      <TableRowCell className={styles.peers}>
        {protocol === 'torrent' ? (
          <Peers seeders={seeders} leechers={leechers} />
        ) : null}
      </TableRowCell>

      <TableRowCell className={styles.languages}>
        <EpisodeLanguages languages={languages} />
      </TableRowCell>

      <TableRowCell className={styles.quality}>
        <EpisodeQuality quality={quality} showRevision={true} />
      </TableRowCell>

      <TableRowCell className={styles.customFormatScore}>
        <Tooltip
          anchor={formatCustomFormatScore(
            customFormatScore,
            customFormats.length
          )}
          tooltip={<EpisodeFormats formats={customFormats} />}
          position={tooltipPositions.LEFT}
        />
      </TableRowCell>

      <TableRowCell className={styles.indexerFlags}>
        {indexerFlags ? (
          <Popover
            anchor={<Icon name={icons.FLAG} />}
            title={translate('IndexerFlags')}
            body={<IndexerFlags indexerFlags={indexerFlags} />}
            position={tooltipPositions.LEFT}
          />
        ) : null}
      </TableRowCell>

      <TableRowCell className={styles.rejected}>
        {rejections.length ? (
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
        ) : null}
      </TableRowCell>

      <TableRowCell className={styles.download}>
        <SpinnerIconButton
          name={getDownloadIcon(isGrabbing, isGrabbed, grabError)}
          kind={getDownloadKind(isGrabbed, grabError)}
          title={getDownloadTooltip(isGrabbing, isGrabbed, grabError)}
          isSpinning={isGrabbing}
          onPress={handleGrabPress}
        />

        <Link
          className={styles.manualDownloadContent}
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
        message={translate('GrabReleaseUnknownSeriesOrEpisodeMessageText', {
          title,
        })}
        confirmLabel={translate('Grab')}
        onConfirm={onGrabConfirm}
        onCancel={onGrabCancel}
      />

      {/* Phase 12 Plan 12-10 — Sub-wave-B-addition (audit row C closure):
          Payload-shape discriminator. Chapter/manga searchPayloads route to
          MangaOverrideMatchModal (manga grab body shape; POSTs to /manga/release
          via useGrabMangaRelease); episode/season payloads route to the existing
          TV OverrideMatchModal (preserved verbatim per D-12-18). Discriminator
          shape `'chapterId' in searchPayload || 'mangaId' in searchPayload`
          mirrors useReleases.ts:385 exactly (Plan 07-05 union-routing pattern).

          Known TODO (v1.1+): chapterIds extraction below defaults to
          `[searchPayload.chapterId]` for ChapterSearchPayload and `[]` for
          MangaSearchPayload (whole-manga search). v1.1+ may tighten with
          separate ChapterOverrideMatchModal vs MangaOverrideMatchModal siblings
          if multi-chapter manga selection becomes a use case (tracked by Plan
          12-98 v1.1-roadmap.md promotion). The mangaId fallback `0` is a
          sentinel — the backend MangaReleaseController.DownloadRelease keys
          off the cached RemoteChapter (indexerId + guid), so the override
          mangaId field is informational; cache resolution drives the actual
          grab. */}
      {isMangaPayload ? (
        <MangaOverrideMatchModal
          isOpen={isOverrideModalOpen}
          title={title}
          indexerId={indexerId}
          guid={guid}
          mangaId={'mangaId' in searchPayload ? searchPayload.mangaId : 0}
          chapterIds={
            'chapterId' in searchPayload ? [searchPayload.chapterId] : []
          }
          protocol={protocol}
          onModalClose={onOverrideModalClose}
        />
      ) : (
        <OverrideMatchModal
          isOpen={isOverrideModalOpen}
          title={title}
          indexerId={indexerId}
          guid={guid}
          seriesId={mappedSeriesId}
          seasonNumber={mappedSeasonNumber}
          episodes={mappedEpisodeInfo}
          languages={languages}
          quality={quality}
          protocol={protocol}
          isGrabbing={tvGrab.isGrabbing}
          grabError={tvGrab.grabError}
          grabRelease={tvGrab.grabRelease}
          onModalClose={onOverrideModalClose}
        />
      )}
    </TableRow>
  );
}

export default InteractiveSearchRow;
