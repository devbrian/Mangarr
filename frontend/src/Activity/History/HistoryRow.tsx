// Sonarr divergence: rewritten as manga-shape ChapterHistory consumer per
// GH issue #73 (Plan 15-12 follow-up) — see DIVERGENCE.md.
// Role-match analog: frontend/src/Wanted/Missing/MissingRow.tsx (canonical
// manga-shape per-row dispatch precedent — same idiom, different column set).
//
// Pre-fix shape: consumed TV `seriesId` / `episodeId` props that the manga
// backend never emits. `useSingleSeries(undefined)` short-circuited; the
// early-return `if (mediaType === 'series' && (!series || !episode)) return null`
// was skipped for manga (F-NEW-2 fix from quick-260507-tff-rerun) but the
// series/episode-specific cells then fell through to empty TableRowCells.
// The result was a row with only "eventType" / "date" / "downloadClient" /
// "indexer" / "sourceTitle" / "details" rendered — the manga-shape title /
// chapter number / chapter title / language columns were blank.
//
// Post-fix shape: consumes `ChapterHistory` props directly via spread from
// `History.tsx`'s `{...item}`. Resolves manga-title link via the hydrated
// `manga` subresource (fallback to `useSingleManga(mangaId)` from the
// `['/manga']` cache). Resolves chapter number / title via the hydrated
// `chapter` subresource (fallback to `useSingleChapter(chapterId)`). Renders
// a `LanguageBadge` from `translatedLanguage`.
//
// Column-key strategy: preserves the legacy TV column keys (`series.sortTitle`,
// `episode`, `episodes.title`) so the persisted Zustand `history_options`
// state of upgrade-path users continues to work without a localStorage
// migration; the column labels are flipped in `historyOptionsStore.ts`.
// Two NEW column keys (`translatedLanguage`, `scanlationGroup`) are added.
//
// Phase 8 cleanup: when Tv/ deletes, the column keys can rename to manga.X /
// chapter.X (one-off Zustand migration at that point).
import React, { useCallback, useState } from 'react';
import ChapterNumber from 'Chapter/ChapterNumber';
import ChapterTitleLink from 'Chapter/ChapterTitleLink';
import LanguageBadge from 'Chapter/LanguageBadge';
import { useSingleChapter } from 'Chapter/useChapter';
import IconButton from 'Components/Link/IconButton';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import { icons } from 'Helpers/Props';
import MangaTitleLink from 'Manga/MangaTitleLink';
import { useSingleManga } from 'Manga/useManga';
import {
  ChapterHistoryEventType,
  ChapterHistorySubresource,
  MangaHistorySubresource,
} from 'typings/ChapterHistory';
import { HistoryData } from 'typings/History';
import translate from 'Utilities/String/translate';
import HistoryDetailsModal from './Details/HistoryDetailsModal';
import HistoryEventTypeCell from './HistoryEventTypeCell';
import styles from './HistoryRow.css';

interface HistoryRowProps {
  id: number;
  mangaId?: number;
  chapterId?: number;
  translatedLanguage?: string;
  scanlationGroup?: string;
  eventType: ChapterHistoryEventType;
  sourceTitle?: string;
  date: string;
  data?: Record<string, string>;
  downloadId?: string;
  manga?: MangaHistorySubresource;
  chapter?: ChapterHistorySubresource;
  columns: Column[];
  mediaType?: 'manga';
}

function HistoryRow(props: HistoryRowProps) {
  const {
    id,
    mangaId,
    chapterId,
    translatedLanguage,
    scanlationGroup,
    eventType,
    sourceTitle,
    date,
    data,
    downloadId,
    manga: hydratedManga,
    chapter: hydratedChapter,
    columns,
  } = props;

  // Prefer the hydrated subresources from the backend payload; fall back to
  // the React Query caches (`['/manga']`, `['/chapter/{id}']`) populated by
  // the sidebar Manga page and any open Manga Details view.
  const lookedUpManga = useSingleManga(mangaId);
  const manga = hydratedManga ?? lookedUpManga;

  const { data: lookedUpChapter } = useSingleChapter(
    hydratedChapter == null && chapterId != null ? chapterId : undefined
  );
  const chapter = hydratedChapter ?? lookedUpChapter;

  const [isDetailsModalOpen, setIsDetailsModalOpen] = useState(false);

  const handleDetailsPress = useCallback(() => {
    setIsDetailsModalOpen(true);
  }, [setIsDetailsModalOpen]);

  const handleDetailsModalClose = useCallback(() => {
    setIsDetailsModalOpen(false);
  }, [setIsDetailsModalOpen]);

  return (
    <TableRow data-testid={`manga-history-row-${id}`}>
      {columns.map((column) => {
        const { name, isVisible } = column;

        if (!isVisible) {
          return null;
        }

        if (name === 'eventType') {
          // Phase 18 Plan-05: `manga-history-row-{id}-decision` is the
          // load-bearing cell per feedback_verify_ui_state_not_just_rendering.md
          // (the icon-cell that hides silent rejection icons). The cell exposes
          // `data-event-type` so fixtures can assert decision state
          // (e.g. grabbed vs downloadFailed) without parsing the icon glyph.
          return (
            <HistoryEventTypeCell
              key={name}
              eventType={eventType as unknown as never}
              data={(data ?? {}) as unknown as HistoryData}
              data-testid={`manga-history-row-${id}-decision`}
            />
          );
        }

        // Column keys preserved verbatim from the pre-fix TV-shape store
        // (`series.sortTitle`, `episode`, `episodes.title`) for upgrade-path
        // Zustand state continuity — labels are relabeled in the store.
        if (name === 'series.sortTitle') {
          // Phase 18 Plan-05: legacy Zustand key kept; canonical manga-shape
          // testid (`-manga`) exposed.
          return (
            <TableRowCell
              key={name}
              data-testid={`manga-history-row-${id}-manga`}
            >
              {manga ? (
                <MangaTitleLink
                  titleSlug={
                    'titleSlug' in manga
                      ? (manga as { titleSlug?: string }).titleSlug
                      : undefined
                  }
                  title={manga.title ?? sourceTitle ?? ''}
                />
              ) : (
                sourceTitle ?? ''
              )}
            </TableRowCell>
          );
        }

        if (name === 'episode') {
          if (!chapter) {
            return (
              <TableRowCell
                key={name}
                data-testid={`manga-history-row-${id}-chapter`}
              />
            );
          }

          return (
            <TableRowCell
              key={name}
              data-testid={`manga-history-row-${id}-chapter`}
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
          // Phase 18 Plan-05: `-quality` testid name preserved per the plan
          // (Mangarr's quality model maps to TranslationProfile per
          // PROJECT.md Out of Scope; the testid is the row-level handle).
          return (
            <TableRowCell
              key={name}
              data-testid={`manga-history-row-${id}-quality`}
            >
              <LanguageBadge language={translatedLanguage} />
            </TableRowCell>
          );
        }

        if (name === 'scanlationGroup') {
          return (
            <TableRowCell key={name}>{scanlationGroup ?? ''}</TableRowCell>
          );
        }

        if (name === 'date') {
          return <RelativeDateCell key={name} date={date} />;
        }

        if (name === 'downloadClient') {
          const downloadClientName =
            data && 'downloadClientName' in data
              ? data.downloadClientName
              : null;
          const downloadClient =
            data && 'downloadClient' in data ? data.downloadClient : null;

          return (
            <TableRowCell key={name} className={styles.downloadClient}>
              {downloadClientName ?? downloadClient ?? ''}
            </TableRowCell>
          );
        }

        if (name === 'indexer') {
          return (
            <TableRowCell key={name} className={styles.indexer}>
              {data && 'indexer' in data ? data.indexer : ''}
            </TableRowCell>
          );
        }

        if (name === 'releaseGroup') {
          // Manga's analog of "release group" is the scanlation group, which
          // travels on the row's top-level `scanlationGroup` field, NOT the
          // GrabbedHistoryData payload. Fall through to the dedicated
          // scanlationGroup column for the canonical render; this column
          // remains in the registry as a no-op for upgrade-path persistence.
          return (
            <TableRowCell key={name} className={styles.releaseGroup}>
              {scanlationGroup ?? ''}
            </TableRowCell>
          );
        }

        if (name === 'sourceTitle') {
          return <TableRowCell key={name}>{sourceTitle ?? ''}</TableRowCell>;
        }

        if (name === 'details') {
          return (
            <TableRowCell key={name} className={styles.details}>
              <IconButton
                name={icons.INFO}
                aria-label={translate('Details')}
                onPress={handleDetailsPress}
              />
            </TableRowCell>
          );
        }

        return null;
      })}

      <HistoryDetailsModal
        id={id}
        isOpen={isDetailsModalOpen}
        eventType={eventType as unknown as never}
        sourceTitle={sourceTitle ?? ''}
        data={(data ?? {}) as unknown as HistoryData}
        downloadId={downloadId}
        onModalClose={handleDetailsModalClose}
      />
    </TableRow>
  );
}

export default HistoryRow;
