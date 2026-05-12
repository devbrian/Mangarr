// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// `{ useMultipleSeries } from 'Series/useSeries'` rewritten to
// `{ useMultipleManga } from 'Manga/useManga'` peer. Call sites in the body
// renamed; the inherited `series`/`sortedSeries` locals keep the TV-shape
// var names since the Tasks-queue command body still references seriesId(s).
import React from 'react';
import { CommandBody } from 'Commands/Command';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { useMultipleManga } from 'Manga/useManga';
import sortByProp from 'Utilities/Array/sortByProp';
import translate from 'Utilities/String/translate';
import styles from './QueuedTaskRowNameCell.css';

function formatTitles(titles: string[]) {
  if (!titles) {
    return null;
  }

  if (titles.length > 11) {
    return (
      <span title={titles.join(', ')}>
        {titles.slice(0, 10).join(', ')}, {titles.length - 10} more
      </span>
    );
  }

  return <span>{titles.join(', ')}</span>;
}

export interface QueuedTaskRowNameCellProps {
  commandName: string;
  body: CommandBody;
  clientUserAgent?: string;
}

export default function QueuedTaskRowNameCell(
  props: QueuedTaskRowNameCellProps
) {
  const { commandName, body, clientUserAgent } = props;
  const seriesIds = 'seriesIds' in body ? [...body.seriesIds] : [];

  if ('seriesId' in body && body.seriesId) {
    seriesIds.push(body.seriesId);
  }

  const series = useMultipleManga(seriesIds);
  const sortedSeries = series.sort(sortByProp('sortTitle'));

  return (
    <TableRowCell>
      <span className={styles.commandName}>
        {commandName}
        {sortedSeries.length ? (
          <span> - {formatTitles(sortedSeries.map((s) => s.title))}</span>
        ) : null}
        {'seasonNumber' in body && body.seasonNumber ? (
          <span>
            {' '}
            {translate('SeasonNumberToken', {
              seasonNumber: body.seasonNumber,
            })}
          </span>
        ) : null}
      </span>

      {clientUserAgent ? (
        <span
          className={styles.userAgent}
          title={translate('TaskUserAgentTooltip')}
        >
          {translate('From')}: {clientUserAgent}
        </span>
      ) : null}
    </TableRowCell>
  );
}
