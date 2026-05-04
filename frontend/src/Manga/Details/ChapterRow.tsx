// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/EpisodeRow.tsx (per-row
// monitor + status + search — the Manga Chapters tab is a flat sortable
// table with no season grouping per PROJECT.md "Volumes/Seasons" Out-of-Scope).
//
// Manga sibling preserves: <TableRow> + per-column dispatch shape;
// MonitorToggleButton + RelativeDateCell + TableRowCell.
// Manga sibling diverges from EpisodeRow:
//   * Drops scene-numbering cells, EpisodeFileLanguages cell, MediaInfo cell,
//     IndexerFlags cell, runtime cell, finaleType — manga has none of these.
//   * Adds LanguageBadge cell + ScanlationGroup cell.
//   * Per-row monitor toggle dispatches PUT /api/v5/chapter/{id} with full
//     chapter body (Plan 07-01 RestPutById endpoint) instead of dispatching
//     into the Sonarr command queue.
//   * `chapterNumber` rendered via the decimal-aware `<ChapterNumber>` cell.
//   * `status` reads from ChapterStatus.tsx (6-state Lock #4 set).
//   * `actions` cell renders ChapterSearchCell (Auto + Interactive search).
//
// T-07-15 (XSS) mitigation: every user-controlled string field flows through
// React JSX default escaping ({chapter.title}, {scanlationGroup}). NO
// dangerouslySetInnerHTML anywhere in this file.
//
// Phase 8 cleanup: collapse with EpisodeRow when Tv/ deletes.
import React, { useCallback } from 'react';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import Chapter from 'Chapter/Chapter';
import ChapterNumber from 'Chapter/ChapterNumber';
import ChapterSearchCell from 'Chapter/ChapterSearchCell';
import ChapterStatus from 'Chapter/ChapterStatus';
import ChapterTitleLink from 'Chapter/ChapterTitleLink';
import LanguageBadge from 'Chapter/LanguageBadge';
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

        if (name === 'translatedLanguage') {
          return (
            <TableRowCell key={name} className={styles.language}>
              <LanguageBadge language={chapter.translatedLanguage} />
            </TableRowCell>
          );
        }

        if (name === 'scanlationGroup') {
          return (
            <TableRowCell key={name} className={styles.scanlationGroup}>
              {chapter.scanlationGroup}
            </TableRowCell>
          );
        }

        if (name === 'releaseDate') {
          return <RelativeDateCell key={name} date={chapter.releaseDate} />;
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
