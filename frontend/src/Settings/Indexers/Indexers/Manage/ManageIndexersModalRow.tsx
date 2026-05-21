import React, { useCallback } from 'react';
import ProtocolLabel from 'Activity/Queue/ProtocolLabel';
import { useSelect } from 'App/Select/SelectContext';
import Label from 'Components/Label';
import MangaTagList from 'Components/MangaTagList';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';
import { kinds } from 'Helpers/Props';
import { IndexerModel } from 'Settings/Indexers/useIndexers';
import { SelectStateInputProps } from 'typings/props';
import translate from 'Utilities/String/translate';
import styles from './ManageIndexersModalRow.css';

interface ManageIndexersModalRowProps {
  id: number;
  name: string;
  protocol: DownloadProtocol;
  enableRss: boolean;
  enableAutomaticSearch: boolean;
  enableInteractiveSearch: boolean;
  priority: number;
  implementation: string;
  tags: number[];
  columns: Column[];
}

function ManageIndexersModalRow(props: ManageIndexersModalRowProps) {
  const {
    id,
    name,
    protocol,
    enableRss,
    enableAutomaticSearch,
    enableInteractiveSearch,
    priority,
    implementation,
    tags,
  } = props;

  const { toggleSelected, useIsSelected } = useSelect<IndexerModel>();
  const isSelected = useIsSelected(id);

  const onSelectedChangeWrapper = useCallback(
    ({ id, value, shiftKey }: SelectStateInputProps) => {
      toggleSelected({
        id,
        isSelected: value,
        shiftKey,
      });
    },
    [toggleSelected]
  );

  // GH #224 fix-forward — per-row testid (`settings-indexer-row-{id}`) +
  // checkbox testid (`settings-indexer-row-{id}-checkbox`) so the
  // ManageIndexersSortAndRangeSelectFixture can pick rows by their seeded
  // backend id. Mirrors the
  // `settings-customformat-row-{_seedId}-checkbox` shape pinned in
  // ManageCustomFormatsEditModalFixture.cs.
  return (
    <TableRow data-testid={`settings-indexer-row-${id}`}>
      <TableSelectCell
        id={id}
        isSelected={isSelected}
        data-testid={`settings-indexer-row-${id}-checkbox`}
        onSelectedChange={onSelectedChangeWrapper}
      />

      <TableRowCell className={styles.name}>{name}</TableRowCell>

      <TableRowCell className={styles.protocol}>
        <ProtocolLabel protocol={protocol} />
      </TableRowCell>

      <TableRowCell className={styles.implementation}>
        {implementation}
      </TableRowCell>

      <TableRowCell className={styles.enableRss}>
        <Label
          kind={enableRss ? kinds.SUCCESS : kinds.DISABLED}
          outline={!enableRss}
        >
          {enableRss ? translate('Yes') : translate('No')}
        </Label>
      </TableRowCell>

      <TableRowCell className={styles.enableAutomaticSearch}>
        <Label
          kind={enableAutomaticSearch ? kinds.SUCCESS : kinds.DISABLED}
          outline={!enableAutomaticSearch}
        >
          {enableAutomaticSearch ? translate('Yes') : translate('No')}
        </Label>
      </TableRowCell>

      <TableRowCell className={styles.enableInteractiveSearch}>
        <Label
          kind={enableInteractiveSearch ? kinds.SUCCESS : kinds.DISABLED}
          outline={!enableInteractiveSearch}
        >
          {enableInteractiveSearch ? translate('Yes') : translate('No')}
        </Label>
      </TableRowCell>

      <TableRowCell className={styles.priority}>{priority}</TableRowCell>

      <TableRowCell className={styles.tags}>
        <MangaTagList tags={tags} />
      </TableRowCell>
    </TableRow>
  );
}

export default ManageIndexersModalRow;
