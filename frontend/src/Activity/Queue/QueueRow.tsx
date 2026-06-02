// Sonarr divergence: rewritten as manga-shape MangaQueueRow consumer per
// GH issue #73 (Plan 15-12 follow-up) — see DIVERGENCE.md.
// Role-match analog: frontend/src/Wanted/Missing/MissingRow.tsx (canonical
// manga-shape per-row dispatch precedent — same idiom, different column set).
//
// Pre-fix shape: consumed TV `seriesId` / `episodeIds` / `seasonNumbers` /
// `quality` / `customFormats` props that the manga backend never emits.
// `useSingleSeries(undefined)` short-circuited to undefined; `useEpisodesWithIds([])`
// returned `[]` (Phase 15 stubs); the cells either rendered empty TableRowCells
// or fell through to `null` — the result was a 5-blank-column row carrying only
// "title" / "Time Left" / actions for every queue item. PR #74 added crash-guards
// (default `[]` for arrays) so the rows rendered at all; this PR completes the
// manga-shape rewrite so the cells actually display values.
//
// Post-fix shape: consumes `MangaQueueItem` props directly via spread from
// `Queue.tsx`'s `{...item}`. Resolves manga-title link via the hydrated
// `manga` subresource (with fallback to `useSingleManga(mangaId)` from the
// `['/manga']` React Query cache). Resolves chapter number / title via the
// hydrated `chapter` subresource. Renders a `LanguageBadge` from
// `translatedLanguage` (single BCP-47 string per Phase 3 D-Q4 — manga has no
// quality model per Phase 5 D-04) and a plain-text `scanlationGroup` column.
//
// Column-key strategy: preserves the legacy TV column keys (`series.sortTitle`,
// `episode`, `episodes.title`) so the persisted Zustand `queue_options` state
// of upgrade-path users continues to work without a localStorage migration;
// the column **labels** in `queueOptionsStore.ts` flip to manga terminology.
// Two NEW column keys (`translatedLanguage`, `scanlationGroup`) are added
// alongside. Mirrors the `MissingRow` / `missingOptionsStore` + `BlocklistRow`
// / `blocklistOptionsStore` precedent — same "preserve key, flip label, drop
// quality/customFormats" pattern locked in by Plan-15-12 era refactors.
//
// Phase 8 cleanup: when Tv/ deletes, the column keys can rename to manga.X /
// chapter.X (one-off Zustand migration at that point).
import React, { useCallback, useState } from 'react';
import ProtocolLabel from 'Activity/Queue/ProtocolLabel';
import { useSelect } from 'App/Select/SelectContext';
import ChapterNumber from 'Chapter/ChapterNumber';
import ChapterTitleLink from 'Chapter/ChapterTitleLink';
import LanguageBadge from 'Chapter/LanguageBadge';
import { useSingleChapter } from 'Chapter/useChapter';
import IconButton from 'Components/Link/IconButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import ProgressBar from 'Components/ProgressBar';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';
import { icons, kinds } from 'Helpers/Props';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';
import MangaTitleLink from 'Manga/MangaTitleLink';
import { useSingleManga } from 'Manga/useManga';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import MangaQueueItem, {
  ChapterQueueSubresource,
  MangaQueueSubresource,
  QueueStatusMessage,
} from 'typings/MangaQueueItem';
import { SelectStateInputProps } from 'typings/props';
import {
  QueueTrackedDownloadState,
  QueueTrackedDownloadStatus,
} from 'typings/Queue';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import QueueStatusCell from './QueueStatusCell';
import RemoveQueueItemModal from './RemoveQueueItemModal';
import TimeLeftCell from './TimeLeftCell';
import { useGrabQueueItem, useRemoveQueueItem } from './useQueue';
import styles from './QueueRow.css';

// MangaQueueItem field-set mirrors `Sonarr.Api.V5.Manga.Queue.MangaQueueResource`
// exactly (Phase 6 Plan 06-09). `columns` is appended by the parent table;
// `onQueueRowModalOpenOrClose` is the modal-block hook passed down from
// `Queue.tsx` so user-opened InteractiveImport / RemoveQueueItem modals can
// pause the SignalR-driven refresh.
interface QueueRowProps {
  id: number;
  mangaId?: number;
  chapterId?: number;
  chapterIds?: number[];
  translatedLanguage?: string;
  scanlationGroup?: string;
  downloadId?: string;
  title?: string;
  status?: string;
  trackedDownloadStatus?: string;
  trackedDownloadState?: string;
  statusMessages?: QueueStatusMessage[];
  errorMessage?: string;
  protocol: DownloadProtocol;
  indexer?: string;
  outputPath?: string;
  downloadClient?: string;
  downloadClientHasPostImportCategory: boolean;
  estimatedCompletionTime?: string;
  added?: string;
  timeLeft?: string;
  size: number;
  sizeLeft: number;
  // Phase 36 Plan 06 (D-01 / LOOP-05): ADDITIVE manga-native page-progress caption. Present only
  // for the in-process client; absent (undefined) on the gateway path (Phase 38) → bytes/% fallback.
  totalPages?: number;
  completedPages?: number;
  manga?: MangaQueueSubresource;
  chapter?: ChapterQueueSubresource;
  columns: Column[];
  onQueueRowModalOpenOrClose: (isOpen: boolean) => void;
}

function QueueRow(props: QueueRowProps) {
  const {
    id,
    mangaId,
    chapterId,
    downloadId,
    title,
    status,
    trackedDownloadStatus,
    trackedDownloadState,
    statusMessages,
    errorMessage,
    translatedLanguage,
    scanlationGroup,
    protocol,
    indexer,
    outputPath,
    downloadClient,
    downloadClientHasPostImportCategory,
    estimatedCompletionTime,
    added,
    timeLeft,
    size,
    sizeLeft,
    totalPages,
    completedPages,
    manga: hydratedManga,
    chapter: hydratedChapter,
    columns,
    onQueueRowModalOpenOrClose,
  } = props;

  // Prefer the hydrated subresources from the backend payload; fall back to
  // the React Query caches (`['/manga']`, `['/chapter/{id}']`) populated by
  // the sidebar Manga page and any open Manga Details view. The hooks return
  // `undefined` until the cache hydrates — the row renders chapter-shape
  // immediately with `chapter?.title` placeholder text in that window
  // (typically <100ms). Mirror of MissingRow's `useSingleManga(mangaId)`
  // fallback pattern.
  const lookedUpManga = useSingleManga(mangaId);
  const manga = hydratedManga ?? lookedUpManga;

  const { data: lookedUpChapter } = useSingleChapter(
    hydratedChapter == null && chapterId != null ? chapterId : undefined
  );
  const chapter = hydratedChapter ?? lookedUpChapter;

  const { showRelativeDates, shortDateFormat, timeFormat } =
    useUiSettingsValues();
  const { removeQueueItem, isRemoving } = useRemoveQueueItem(id);
  const { grabQueueItem, isGrabbing, grabError } = useGrabQueueItem(id);
  const { toggleSelected, useIsSelected } = useSelect<MangaQueueItem>();
  const isSelected = useIsSelected(id);

  const [isRemoveQueueItemModalOpen, setIsRemoveQueueItemModalOpen] =
    useState(false);

  const [isInteractiveImportModalOpen, setIsInteractiveImportModalOpen] =
    useState(false);

  const handleGrabPress = useCallback(() => {
    grabQueueItem();
  }, [grabQueueItem]);

  const handleInteractiveImportPress = useCallback(() => {
    onQueueRowModalOpenOrClose(true);
    setIsInteractiveImportModalOpen(true);
  }, [setIsInteractiveImportModalOpen, onQueueRowModalOpenOrClose]);

  const handleInteractiveImportModalClose = useCallback(() => {
    onQueueRowModalOpenOrClose(false);
    setIsInteractiveImportModalOpen(false);
  }, [setIsInteractiveImportModalOpen, onQueueRowModalOpenOrClose]);

  const handleRemoveQueueItemPress = useCallback(() => {
    onQueueRowModalOpenOrClose(true);
    setIsRemoveQueueItemModalOpen(true);
  }, [setIsRemoveQueueItemModalOpen, onQueueRowModalOpenOrClose]);

  const handleRemoveQueueItemModalConfirmed = useCallback(() => {
    onQueueRowModalOpenOrClose(false);
    removeQueueItem();
    setIsRemoveQueueItemModalOpen(false);
  }, [
    setIsRemoveQueueItemModalOpen,
    removeQueueItem,
    onQueueRowModalOpenOrClose,
  ]);

  const handleRemoveQueueItemModalClose = useCallback(() => {
    onQueueRowModalOpenOrClose(false);
    setIsRemoveQueueItemModalOpen(false);
  }, [setIsRemoveQueueItemModalOpen, onQueueRowModalOpenOrClose]);

  const handleSelectedChange = useCallback(
    ({ id, value, shiftKey = false }: SelectStateInputProps) => {
      toggleSelected({
        id,
        isSelected: value,
        shiftKey,
      });
    },
    [toggleSelected]
  );

  const progress = size > 0 ? 100 - (sizeLeft / size) * 100 : 0;
  const showInteractiveImport =
    status === 'completed' && trackedDownloadStatus === 'warning';
  const isPending =
    status === 'delay' || status === 'downloadClientUnavailable';

  return (
    <TableRow data-testid={`manga-queue-row-${id}`}>
      <TableSelectCell
        id={id}
        isSelected={isSelected}
        onSelectedChange={handleSelectedChange}
      />

      {columns.map((column) => {
        const { name, isVisible } = column;

        if (!isVisible) {
          return null;
        }

        if (name === 'status') {
          return (
            <QueueStatusCell
              key={name}
              sourceTitle={title ?? ''}
              status={status ?? ''}
              trackedDownloadStatus={
                trackedDownloadStatus as QueueTrackedDownloadStatus | undefined
              }
              trackedDownloadState={
                trackedDownloadState as QueueTrackedDownloadState | undefined
              }
              statusMessages={statusMessages}
              errorMessage={errorMessage}
              data-testid={`manga-queue-row-${id}-status`}
            />
          );
        }

        // Column keys preserved verbatim from the pre-fix TV-shape store
        // (`series.sortTitle`, `episode`, `episodes.title`) so the existing
        // queueOptionsStore Zustand persisted state continues to work without
        // a localStorage migration — labels are relabeled in the store.
        // Phase 8 cleanup: rename keys to manga.X / chapter.X when Tv/ deletes.
        if (name === 'series.sortTitle') {
          // Phase 18 Plan-05: column key `series.sortTitle` is the upgrade-path
          // Zustand key — the data-testid uses the canonical manga-shape name
          // (`-manga`) per the data-testid-spec naming convention.
          return (
            <TableRowCell
              key={name}
              data-testid={`manga-queue-row-${id}-manga`}
            >
              {manga ? (
                <MangaTitleLink
                  titleSlug={
                    'titleSlug' in manga
                      ? (manga as { titleSlug?: string }).titleSlug
                      : undefined
                  }
                  title={manga.title ?? title ?? ''}
                />
              ) : (
                title
              )}
            </TableRowCell>
          );
        }

        if (name === 'episode') {
          if (!chapter) {
            return (
              <TableRowCell
                key={name}
                data-testid={`manga-queue-row-${id}-chapter`}
              />
            );
          }

          return (
            <TableRowCell
              key={name}
              data-testid={`manga-queue-row-${id}-chapter`}
            >
              <ChapterNumber chapterNumber={chapter.chapterNumber} />
            </TableRowCell>
          );
        }

        if (name === 'episodes.title') {
          if (!chapter || chapter.mangaId == null) {
            return <TableRowCell key={name} />;
          }

          return (
            <TableRowCell key={name}>
              <ChapterTitleLink
                chapterId={chapter.id}
                mangaId={chapter.mangaId}
                chapterTitle={chapter.title}
              />
            </TableRowCell>
          );
        }

        if (name === 'translatedLanguage') {
          return (
            <TableRowCell key={name}>
              <LanguageBadge language={translatedLanguage} />
            </TableRowCell>
          );
        }

        if (name === 'scanlationGroup') {
          return (
            <TableRowCell key={name}>{scanlationGroup ?? ''}</TableRowCell>
          );
        }

        if (name === 'protocol') {
          return (
            <TableRowCell key={name}>
              <ProtocolLabel protocol={protocol} />
            </TableRowCell>
          );
        }

        if (name === 'indexer') {
          return <TableRowCell key={name}>{indexer}</TableRowCell>;
        }

        if (name === 'downloadClient') {
          return <TableRowCell key={name}>{downloadClient}</TableRowCell>;
        }

        if (name === 'title') {
          return <TableRowCell key={name}>{title}</TableRowCell>;
        }

        if (name === 'size') {
          return <TableRowCell key={name}>{formatBytes(size)}</TableRowCell>;
        }

        if (name === 'outputPath') {
          return <TableRowCell key={name}>{outputPath}</TableRowCell>;
        }

        if (name === 'estimatedCompletionTime') {
          return (
            <TimeLeftCell
              key={name}
              status={status ?? ''}
              estimatedCompletionTime={estimatedCompletionTime}
              timeLeft={timeLeft}
              size={size}
              sizeLeft={sizeLeft}
              showRelativeDates={showRelativeDates}
              shortDateFormat={shortDateFormat}
              timeFormat={timeFormat}
            />
          );
        }

        if (name === 'progress') {
          // Phase 36 Plan 06 (D-01 / LOOP-05): render a manga-native "page X/Y" caption when the
          // in-process client reports page counts (`totalPages != null`). The byte/%-driven
          // `progress` value (size>0-guarded from Plan 02) STILL drives the ProgressBar fill
          // (D-01b — presentational caption only). When `totalPages` is absent (the gateway path,
          // Phase 38), the row renders the EXISTING bytes/% behavior unchanged (graceful fallback,
          // D-01a). The ETA/timeleft cell is untouched (D-02).
          return (
            <TableRowCell
              key={name}
              className={styles.progress}
              data-testid={`manga-queue-row-${id}-progress`}
            >
              {!!progress && (
                <ProgressBar
                  progress={progress}
                  title={`${progress.toFixed(1)}%`}
                />
              )}

              {totalPages != null ? (
                <div data-testid={`manga-queue-row-${id}-page-caption`}>
                  {translate('PageProgress', {
                    completedPages: completedPages ?? 0,
                    totalPages,
                  })}
                </div>
              ) : null}
            </TableRowCell>
          );
        }

        if (name === 'added') {
          return <RelativeDateCell key={name} date={added} />;
        }

        if (name === 'actions') {
          return (
            <TableRowCell key={name} className={styles.actions}>
              {showInteractiveImport ? (
                <IconButton
                  name={icons.INTERACTIVE}
                  aria-label={translate('InteractiveSearch')}
                  onPress={handleInteractiveImportPress}
                />
              ) : null}

              {isPending ? (
                <SpinnerIconButton
                  name={icons.DOWNLOAD}
                  kind={grabError ? kinds.DANGER : kinds.DEFAULT}
                  aria-label={translate('Grab')}
                  isSpinning={isGrabbing}
                  onPress={handleGrabPress}
                />
              ) : null}

              <SpinnerIconButton
                title={translate('RemoveFromQueue')}
                name={icons.REMOVE}
                isSpinning={isRemoving}
                onPress={handleRemoveQueueItemPress}
              />
            </TableRowCell>
          );
        }

        return null;
      })}

      <InteractiveImportModal
        isOpen={isInteractiveImportModalOpen}
        downloadIds={downloadId ? [downloadId] : []}
        title={title ?? ''}
        onModalClose={handleInteractiveImportModalClose}
      />

      <RemoveQueueItemModal
        isOpen={isRemoveQueueItemModalOpen}
        sourceTitle={title ?? ''}
        canChangeCategory={!!downloadClientHasPostImportCategory}
        canIgnore={!!manga}
        isPending={isPending}
        onRemovePress={handleRemoveQueueItemModalConfirmed}
        onModalClose={handleRemoveQueueItemModalClose}
      />
    </TableRow>
  );
}

export default QueueRow;
