// Sonarr divergence: NEW manga sibling — wires the per-manga History tab against
// /api/v5/manga/history?mangaIds=<id> (Phase 6 Plan 06-09 backend; was a placeholder
// in MangaDetails.tsx until the manga-tabs-empty debug session). Role-match
// analog: TV `useSeriesHistory.ts`. See DIVERGENCE.md.
//
// Phase 8 cleanup: collapse with the Series equivalent when Tv/ deletes.
import React, { useMemo } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { kinds } from 'Helpers/Props';
import ChapterHistory from 'typings/ChapterHistory';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';

interface MangaDetailsHistoryProps {
  mangaId: number;
}

const COLUMNS: Column[] = [
  {
    name: 'eventType',
    label: () => translate('EventType'),
    isVisible: true,
    isSortable: false,
  },
  {
    name: 'sourceTitle',
    label: () => translate('SourceTitle'),
    isVisible: true,
    isSortable: false,
  },
  {
    name: 'chapter',
    label: () => translate('Chapter'),
    isVisible: true,
    isSortable: false,
  },
  {
    name: 'language',
    label: () => translate('TranslatedLanguage'),
    isVisible: true,
    isSortable: false,
  },
  {
    name: 'scanlationGroup',
    label: () => translate('ScanlationGroup'),
    isVisible: true,
    isSortable: false,
  },
  {
    name: 'date',
    label: () => translate('Date'),
    isVisible: true,
    isSortable: false,
  },
];

const PAGE_SIZE = 50;

function MangaDetailsHistory({ mangaId }: MangaDetailsHistoryProps) {
  // Backend: GET /api/v5/manga/history?mangaIds={id}&pageSize=50&page=1 (Phase 6).
  // The PropertyFilter shape (key+value+type) is the canonical filter wire format
  // used by every paged endpoint; getQueryString translates the array `value: [id]`
  // to a single `?mangaIds=<id>` query param (each array element appended once).
  // ASP.NET Core binds the resulting single `mangaIds` query param to a List<int>
  // with one entry, which the controller filter pipeline then matches against
  // ChapterHistory.MangaId per Phase 6 Plan 06-09.
  const filters = useMemo(
    () => [
      {
        key: 'mangaIds',
        value: [mangaId],
        type: 'equal' as const,
      },
    ],
    [mangaId]
  );

  const { records, isFetching, isFetched, error } =
    usePagedApiQuery<ChapterHistory>({
      path: '/manga/history',
      page: 1,
      pageSize: PAGE_SIZE,
      sortKey: 'date',
      sortDirection: 'descending',
      filters,
      queryParams: {
        // Hydrate manga + chapter subresources so we can display chapter number
        // / title / language without a second round-trip.
        includeSubresources: ['manga', 'chapter'],
      },
    });

  if (isFetching && !isFetched) {
    return <LoadingIndicator />;
  }

  if (error) {
    return (
      <Alert kind={kinds.DANGER}>
        {getErrorMessage(error) ?? translate('HistoryLoadError')}
      </Alert>
    );
  }

  const rows: ChapterHistory[] = records;

  if (rows.length === 0) {
    return <Alert kind={kinds.INFO}>{translate('NoHistoryForThisManga')}</Alert>;
  }

  return (
    <Table columns={COLUMNS}>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.id}>
            <TableRowCell>{row.eventType}</TableRowCell>
            <TableRowCell>{row.sourceTitle ?? ''}</TableRowCell>
            <TableRowCell>
              {row.chapter
                ? `Ch. ${row.chapter.chapterNumber}${row.chapter.title ? ` — ${row.chapter.title}` : ''}`
                : ''}
            </TableRowCell>
            <TableRowCell>
              {row.translatedLanguage?.toUpperCase() ?? ''}
            </TableRowCell>
            <TableRowCell>{row.scanlationGroup ?? ''}</TableRowCell>
            <RelativeDateCell date={row.date} />
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

export default MangaDetailsHistory;
