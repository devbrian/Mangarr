// Sonarr divergence: NEW manga sibling — wires the per-manga Files tab against
// /api/v5/ChapterFile?mangaId=<id> (Phase 13 Plan 13-07 backend; was a placeholder
// in MangaDetails.tsx until the manga-tabs-empty debug session). Role-match
// analog: per-series files panel in `Series/Details/` (TV-side equivalent has
// its own table layout). See DIVERGENCE.md.
//
// Phase 8 cleanup: collapse with the Series equivalent when Tv/ deletes.
import React, { useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';

interface MangaDetailsFilesProps {
  mangaId: number;
}

interface ChapterFile {
  id: number;
  mangaId: number;
  chapterId: number;
  relativePath?: string;
  path?: string;
  size: number;
  dateAdded: string;
  translatedLanguage?: string;
  scanlationGroup?: string;
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
  const { data, isFetching, isFetched, error } = useApiQuery<ChapterFile[]>({
    path: '/chapterFile',
    queryParams: { mangaId },
    queryOptions: {
      staleTime: 30 * 1000,
    },
  });

  const [sortKey, setSortKey] = useState<string>('dateAdded');
  const [sortDirection, setSortDirection] =
    useState<SortDirection>('descending');

  const files = data ?? [];

  const sortedFiles = useMemo(() => {
    const direction = sortDirection === 'ascending' ? 1 : -1;
    return [...files].sort((a, b) => {
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
  }, [files, sortKey, sortDirection]);

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
          <TableRow key={file.id}>
            <TableRowCell>{file.relativePath ?? file.path ?? ''}</TableRowCell>
            <TableRowCell>{formatBytes(file.size)}</TableRowCell>
            <TableRowCell>
              {file.translatedLanguage?.toUpperCase() ?? ''}
            </TableRowCell>
            <TableRowCell>{file.scanlationGroup ?? ''}</TableRowCell>
            <RelativeDateCell date={file.dateAdded} />
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

export default MangaDetailsFiles;
