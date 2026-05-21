// Phase 27.1 Plan 27.1-04 Task 2 — Manage subtree row. 1:1 mirror of
// frontend/src/Settings/Indexers/Indexers/Manage/ManageIndexersModalRow.tsx
// per PATTERNS §Manage Subtree substitution table, with manga-shape fields:
// IndexerModel becomes ImportListModel; Indexer-only flag fields are dropped
// (enableAutomaticAdd / rootFolderPath / translationProfileId added in their
// place); Translation profile name is rendered via useTranslationProfile (manga
// Phase 5 D-04). MangaTagList preserved per Pitfall 3 / Pattern kappa
// (zero TV-shape testid / hook leaks on any new ImportLists file).
//
// GH #224 fix-forward — per-row testid (`settings-importlist-row-{id}`) +
// checkbox testid (`settings-importlist-row-{id}-checkbox`) so the
// ManageImportListsSortAndRangeSelectFixture can pick rows by their seeded
// backend id. Mirrors the
// `settings-customformat-row-{_seedId}-checkbox` shape pinned in
// ManageCustomFormatsEditModalFixture.cs.
import React, { useCallback } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import Label from 'Components/Label';
import MangaTagList from 'Components/MangaTagList';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import { ImportListModel } from 'Settings/ImportLists/useImportLists';
import { useTranslationProfile } from 'Settings/Profiles/Translations/useTranslationProfiles';
import { SelectStateInputProps } from 'typings/props';
import translate from 'Utilities/String/translate';
import styles from './ManageImportListsModalRow.css';

interface ManageImportListsModalRowProps {
  id: number;
  name: string;
  enableAutomaticAdd: boolean;
  rootFolderPath: string;
  translationProfileId: number;
  implementation: string;
  tags: number[];
  columns: Column[];
}

function ManageImportListsModalRow(props: ManageImportListsModalRowProps) {
  const {
    id,
    name,
    enableAutomaticAdd,
    rootFolderPath,
    translationProfileId,
    implementation,
    tags,
  } = props;

  const translationProfile = useTranslationProfile(translationProfileId);
  const translationProfileName = translationProfile?.name ?? '';

  const { toggleSelected, useIsSelected } = useSelect<ImportListModel>();
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

  return (
    <TableRow data-testid={`settings-importlist-row-${id}`}>
      <TableSelectCell
        id={id}
        isSelected={isSelected}
        onSelectedChange={onSelectedChangeWrapper}
        data-testid={`settings-importlist-row-${id}-checkbox`}
      />

      <TableRowCell className={styles.name}>{name}</TableRowCell>

      <TableRowCell className={styles.implementation}>
        {implementation}
      </TableRowCell>

      <TableRowCell className={styles.enableAutomaticAdd}>
        <Label
          kind={enableAutomaticAdd ? kinds.SUCCESS : kinds.DISABLED}
          outline={!enableAutomaticAdd}
        >
          {enableAutomaticAdd ? translate('Yes') : translate('No')}
        </Label>
      </TableRowCell>

      <TableRowCell className={styles.rootFolderPath}>
        {rootFolderPath}
      </TableRowCell>

      <TableRowCell className={styles.translationProfileId}>
        {translationProfileName}
      </TableRowCell>

      <TableRowCell className={styles.tags}>
        <MangaTagList tags={tags} />
      </TableRowCell>
    </TableRow>
  );
}

export default ManageImportListsModalRow;
