// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/Details/EpisodeRow.tsx (per-row
// monitor + status + search — the Manga Chapters tab is a flat sortable
// table with no season grouping per PROJECT.md "Volumes/Seasons" Out-of-Scope).
//
// Canonical row layout: monitored / chapterNumber / title / status / actions.
// Status cell reads aggregate state from ChapterStatus.tsx (Sonarr-canonical
// `monitored && !hasFile` predicate for the Missing pill).
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
//   * `actions` cell renders ChapterSearchCell (Auto + Interactive search)
//     with className={styles.actions} to pin the two buttons side-by-side
//     (fixed width + white-space: nowrap — debug: manga-details-buttons-tvdb).
//
// T-07-15 (XSS) mitigation: every user-controlled string field flows through
// React JSX default escaping ({chapter.title}). NO dangerouslySetInnerHTML
// anywhere in this file.
//
// Phase 8 cleanup: collapse with EpisodeRow when Tv/ deletes.
import React, { useCallback } from 'react';
import Chapter from 'Chapter/Chapter';
import ChapterNumber from 'Chapter/ChapterNumber';
import ChapterSearchCell from 'Chapter/ChapterSearchCell';
import ChapterStatus from 'Chapter/ChapterStatus';
import ChapterTitleLink from 'Chapter/ChapterTitleLink';
import { useToggleChapterMonitored } from 'Chapter/useChapter';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
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
    <TableRow data-testid={`chapter-row-${chapter.id}`}>
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
                data-testid={`chapter-row-${chapter.id}-monitor-toggle`}
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
          // BL-05 (18-REVIEW): annotate the status (file) cell with a per-row
          // testid so the Phase 18 ChapterFileColumnFixture can assert column
          // shape integrity per chapter row. The status pill carries the
          // file-state predicate (Phase 16.1 REVERT-07 + Pitfall 5: 6-state
          // precedence; "File" pill fires when chapterFileId != null), making
          // it the canonical anchor for the GET /api/v5/chapterfile contract.
          return (
            <TableRowCell
              key={name}
              className={styles.status}
              data-testid={`chapter-row-${chapter.id}-file`}
            >
              <ChapterStatus chapter={chapter} />
            </TableRowCell>
          );
        }

        if (name === 'actions') {
          return (
            <ChapterSearchCell
              key={name}
              className={styles.actions}
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
