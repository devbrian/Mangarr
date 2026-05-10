// Sonarr divergence: NEW manga sibling per Phase 7 D-03 — see DIVERGENCE.md.
// Role-match analog: NO direct analog — Sonarr's `Series/Details/SeriesDetailsSeason`
// nests episodes under collapsible season cards; Phase 7 D-03 + PROJECT.md
// "Volumes/Seasons" Out-of-Scope mandates a FLAT sortable table for chapters.
// Closest precedent: `frontend/src/Wanted/Missing/Missing.tsx` (sortable flat
// table with per-row monitor + search affordances).
//
// Column layout: 5 columns (monitored, chapterNumber, title, status, actions).
// Canonical Chapter row is language-free; per-translation breadth is not
// surfaced in the chapters table (manga-domain decision — translation
// preference belongs in QualityProfile/ReleaseProfile via preferred terms,
// not a per-row column).
//
// Manga sibling preserves: Table + TableBody + TableHeader render shape;
// react-query-driven data flow.
// Manga sibling diverges from SeriesDetailsSeason:
//   * NO season grouping. UI-04 + PROJECT.md "Volumes/Seasons" Out-of-Scope
//     drive the flat-list shape; introducing season grouping here is a
//     hard-no anti-pattern (UI-SPEC §Anti-pattern 5).
//   * Client-side sort/filter (no server pagination per RESEARCH Open
//     Question 2 lean) over the manga-specific columns: monitored, chapter
//     number, title, status, actions.
//
// Phase 8 cleanup: nothing to collapse — this stays.
import React, { useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import Chapter from 'Chapter/Chapter';
import { useChaptersByManga } from 'Chapter/useChapter';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import ChapterRow from './ChapterRow';

interface MangaDetailsChaptersProps {
  mangaId: number;
}

const DEFAULT_COLUMNS: Column[] = [
  {
    name: 'monitored',
    label: '',
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'chapterNumber',
    label: '#',
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'title',
    label: () => translate('Title'),
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'status',
    label: () => translate('Status'),
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'actions',
    label: () => translate('Actions'),
    isVisible: true,
    isSortable: false,
  },
];

function getSortValue(chapter: Chapter, sortKey: string): unknown {
  switch (sortKey) {
    case 'monitored':
      return chapter.monitored ? 1 : 0;
    case 'chapterNumber':
      return chapter.chapterNumber;
    case 'title':
      return chapter.title?.toLowerCase() ?? '';
    case 'status':
      // Bucket the status into a stable sort key matching the Lock #4
      // precedence ordering (lower = higher in the list).
      if (chapter.chapterFileId != null) {
        return 2; // have-file
      }
      if (chapter.monitored) {
        return 3; // wanted
      }
      return 5; // unmonitored
    default:
      return chapter.chapterNumber;
  }
}

function MangaDetailsChapters({ mangaId }: MangaDetailsChaptersProps) {
  // Backend: GET /api/v5/chapter?mangaId={mangaId} (Plan 07-01 endpoint).
  // The useApiQuery composes the same URL via path '/chapter' + queryParams
  // { mangaId } — see useChaptersByManga in Chapter/useChapter.ts.
  const { data: chapters, isFetching, isFetched, error } =
    useChaptersByManga(mangaId);

  const [sortKey, setSortKey] = useState<string>('chapterNumber');
  const [sortDirection, setSortDirection] = useState<SortDirection>('descending');

  const sortedChapters = useMemo(() => {
    const direction = sortDirection === 'ascending' ? 1 : -1;
    return [...chapters].sort((a, b) => {
      const aValue = getSortValue(a, sortKey);
      const bValue = getSortValue(b, sortKey);

      if (aValue == null && bValue == null) {
        return 0;
      }
      if (aValue == null) {
        return 1 * direction;
      }
      if (bValue == null) {
        return -1 * direction;
      }
      if (aValue < bValue) {
        return -1 * direction;
      }
      if (aValue > bValue) {
        return 1 * direction;
      }
      return 0;
    });
  }, [chapters, sortKey, sortDirection]);

  const handleSortPress = (name: string, direction?: SortDirection) => {
    setSortKey(name);
    if (direction) {
      setSortDirection(direction);
    } else {
      setSortDirection((prev) =>
        sortKey === name && prev === 'ascending' ? 'descending' : 'ascending'
      );
    }
  };

  if (isFetching && !isFetched) {
    return <LoadingIndicator />;
  }

  if (error) {
    return (
      <Alert kind={kinds.DANGER}>
        {getErrorMessage(error) ?? translate('ChaptersLoadError')}
      </Alert>
    );
  }

  if (chapters.length === 0) {
    // UI-SPEC §Empty states (locked copy):
    //   heading: "No chapters found"
    //   body:    "Mangarr hasn't synced chapter metadata yet. Click "Refresh"
    //            in the toolbar or wait for the next scheduled refresh."
    return (
      <Alert kind={kinds.WARNING}>
        <strong>{translate('NoChaptersFound')}</strong>
        <div>{translate('NoChaptersFoundHint')}</div>
      </Alert>
    );
  }

  return (
    <Table
      columns={DEFAULT_COLUMNS}
      sortKey={sortKey}
      sortDirection={sortDirection}
      onSortPress={handleSortPress}
    >
      <TableBody>
        {sortedChapters.map((chapter) => (
          <ChapterRow
            key={chapter.id}
            chapter={chapter}
            columns={DEFAULT_COLUMNS}
          />
        ))}
      </TableBody>
    </Table>
  );
}

export default MangaDetailsChapters;
