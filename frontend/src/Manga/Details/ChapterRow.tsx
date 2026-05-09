// Sonarr divergence: REWRITTEN per Phase 16 STRUCT-09 + D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/EpisodeRow.tsx (per-row
// monitor + status + search — the Manga Chapters tab is a flat sortable
// table with no season grouping per PROJECT.md "Volumes/Seasons" Out-of-Scope).
//
// Pre-Phase-16: 3 cell branches for translatedLanguage / scanlationGroup / releaseDate.
// Post-Phase-16: those branches dropped; canonical row layout is monitored / chapterNumber
// / title / status / actions. Status cell unchanged (state-machine flip lives in
// frontend/src/Chapter/ChapterStatus.tsx per Phase 16 D-04 + Pitfall 5).
//
// Manga sibling preserves: <TableRow> + per-column dispatch shape;
// MonitorToggleButton + TableRowCell.
// Manga sibling diverges from EpisodeRow:
//   * Drops scene-numbering cells, EpisodeFileLanguages cell, MediaInfo cell,
//     IndexerFlags cell, runtime cell, finaleType — manga has none of these.
//   * Per-row monitor toggle dispatches PUT /api/v5/chapter/{id} with full
//     chapter body (Plan 07-01 RestPutById endpoint) instead of dispatching
//     into the Sonarr command queue.
//   * `chapterNumber` rendered via the decimal-aware `<ChapterNumber>` cell.
//   * `status` reads from ChapterStatus.tsx (6-state Lock #4 set).
//   * `actions` cell renders ChapterSearchCell (Auto + Interactive search).
//
// T-07-15 (XSS) mitigation: every user-controlled string field flows through
// React JSX default escaping ({chapter.title}). NO dangerouslySetInnerHTML
// anywhere in this file.
//
// Phase 8 cleanup: collapse with EpisodeRow when Tv/ deletes.
import React, { useCallback } from 'react';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import Chapter from 'Chapter/Chapter';
import ChapterNumber from 'Chapter/ChapterNumber';
import ChapterSearchCell from 'Chapter/ChapterSearchCell';
import ChapterStatus from 'Chapter/ChapterStatus';
import ChapterTitleLink from 'Chapter/ChapterTitleLink';
import { useToggleChapterMonitored } from 'Chapter/useChapter';
import { useSingleManga } from 'Manga/useManga';
import styles from './ChapterRow.css';

export interface ChapterRowProps {
  chapter: Chapter;
  columns: Column[];
}

function ChapterRow({ chapter, columns }: ChapterRowProps) {
  const manga = useSingleManga(chapter.mangaId);
  const mangaMonitored = manga?.monitored ?? true;

  const { toggleChapterMonitored, isToggling } =
    useToggleChapterMonitored(chapter);

  const handleMonitorPress = useCallback(
    (monitored: boolean) => {
      toggleChapterMonitored(monitored);
    },
    [toggleChapterMonitored]
  );

  return (
    <TableRow>
      {columns.map((column) => {
        const { name, isVisible } = column;

        if (!isVisible) {
          return null;
        }

        if (name === 'monitored') {
          return (
            <TableRowCell key={name} className={styles.monitored}>
              <MonitorToggleButton
                monitored={chapter.monitored}
                isDisabled={!mangaMonitored}
                isSaving={isToggling}
                onPress={handleMonitorPress}
              />
            </TableRowCell>
          );
        }

        if (name === 'chapterNumber') {
          return (
            <TableRowCell key={name} className={styles.chapterNumber}>
              <ChapterNumber
                chapterNumber={chapter.chapterNumber}
                absoluteChapterNumber={chapter.absoluteChapterNumber}
                volumeNumber={chapter.volumeNumber}
              />
            </TableRowCell>
          );
        }

        if (name === 'title') {
          return (
            <TableRowCell key={name} className={styles.title}>
              <ChapterTitleLink
                chapterId={chapter.id}
                mangaId={chapter.mangaId}
                chapterTitle={chapter.title}
              />
            </TableRowCell>
          );
        }

        if (name === 'status') {
          return (
            <TableRowCell key={name} className={styles.status}>
              <ChapterStatus chapter={chapter} />
            </TableRowCell>
          );
        }

        if (name === 'actions') {
          return (
            <ChapterSearchCell
              key={name}
              chapterId={chapter.id}
              mangaId={chapter.mangaId}
              chapterTitle={chapter.title}
            />
          );
        }

        return null;
      })}
    </TableRow>
  );
}

export default ChapterRow;
