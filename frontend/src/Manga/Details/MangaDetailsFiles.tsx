// Sonarr divergence: NEW manga sibling — wires the per-manga Files tab against
// /api/v5/ChapterFile?mangaId=<id> (Phase 13 Plan 13-07 backend; was a placeholder
// in MangaDetails.tsx until the manga-tabs-empty debug session). Role-match
// analog: per-series files panel in `Series/Details/` (TV-side equivalent has
// its own table layout). See DIVERGENCE.md.
//
// issue #84 (2026-05-12): the previously-inline `ChapterFile` interface +
// inline per-row cell composition + bare `useApiQuery` call were extracted
// into the new `ChapterFile/` peer dir (Option A — formal frontend peer dir
// mirroring backend entity). This file now retains table-shell concerns only
// (sort state, error/loading/empty branches, column definitions).
//
// Phase 8 cleanup: collapse with the Series equivalent when Tv/ deletes.
import React, { useCallback, useMemo, useState } from 'react';
import ChapterFile from 'ChapterFile/ChapterFile';
import ChapterFileRow from 'ChapterFile/ChapterFileRow';
import { useChapterFilesByManga } from 'ChapterFile/useChapterFile';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';

interface MangaDetailsFilesProps {
  mangaId: number;
}

const COLUMNS: Column[] = [
  {
    name: 'relativePath',
    label: () => translate('Path'),
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'size',
    label: () => translate('Size'),
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'translatedLanguage',
    label: () => translate('TranslatedLanguage'),
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'scanlationGroup',
    label: () => translate('ScanlationGroup'),
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'dateAdded',
    label: () => translate('DateAdded'),
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'actions',
    label: () => '',
    isVisible: true,
    isSortable: false,
  },
];

function getSortValue(file: ChapterFile, sortKey: string): unknown {
  switch (sortKey) {
    case 'relativePath':
      return file.relativePath?.toLowerCase() ?? '';
    case 'size':
      return file.size;
    case 'translatedLanguage':
      return file.translatedLanguage?.toLowerCase() ?? '';
    case 'scanlationGroup':
      return file.scanlationGroup?.toLowerCase() ?? '';
    case 'dateAdded':
      return file.dateAdded ?? '';
    default:
      return file.dateAdded ?? '';
  }
}

function MangaDetailsFiles({ mangaId }: MangaDetailsFilesProps) {
  // Backend: GET /api/v5/ChapterFile?mangaId={mangaId} (Phase 13 Plan 13-07).
  // Returns a flat list of ChapterFileResource for the manga. ASP.NET Core's
  // case-insensitive routing means `/chapterFile` resolves to the same controller
  // as `/ChapterFile`; we use the lowercase form for consistency with the rest of
  // the manga API surface.
  const { data, isFetching, isFetched, error } =
    useChapterFilesByManga(mangaId);

  const [sortKey, setSortKey] = useState<string>('dateAdded');
  const [sortDirection, setSortDirection] =
    useState<SortDirection>('descending');

  const files = data;

  const sortedFiles = useMemo(() => {
    const direction = sortDirection === 'ascending' ? 1 : -1;
    return [...files].sort((a, b) => {
      const aValue = getSortValue(a, sortKey);
      const bValue = getSortValue(b, sortKey);
      if (aValue == null && bValue == null) {
        return 0;
      }
      if (aValue == null) {
        return Number(direction);
      }
      if (bValue == null) {
        return -1 * direction;
      }
      if (aValue < bValue) {
        return -1 * direction;
      }
      if (aValue > bValue) {
        return Number(direction);
      }
      return 0;
    });
  }, [files, sortKey, sortDirection]);

  const handleSortPress = useCallback(
    (name: string, direction?: SortDirection) => {
      setSortKey(name);
      if (direction) {
        setSortDirection(direction);
      } else {
        setSortDirection((prev) =>
          sortKey === name && prev === 'ascending' ? 'descending' : 'ascending'
        );
      }
    },
    [sortKey, setSortKey, setSortDirection]
  );

  if (isFetching && !isFetched) {
    return <LoadingIndicator />;
  }

  if (error) {
    return (
      <Alert kind={kinds.DANGER}>
        {getErrorMessage(error) ?? translate('ChapterFilesLoadError')}
      </Alert>
    );
  }

  if (files.length === 0) {
    return <Alert kind={kinds.INFO}>{translate('NoChapterFilesYet')}</Alert>;
  }

  return (
    <Table
      columns={COLUMNS}
      sortKey={sortKey}
      sortDirection={sortDirection}
      onSortPress={handleSortPress}
    >
      <TableBody>
        {sortedFiles.map((file) => (
          <ChapterFileRow key={file.id} file={file} />
        ))}
      </TableBody>
    </Table>
  );
}

export default MangaDetailsFiles;
