// Phase 27.1 Plan 27.1-03 (IL-EXCLUSIONS / D-01) — Sonarr-canonical rewrite
// of the Phase 26 substrate placeholder. Replaces the inline `records.map`
// inline-div layout with the Sonarr v5-develop verbatim shape:
// `<SelectProvider> + <Table> + <TablePager>` with three sortable ID columns
// (MangaDex ID / MAL ID / AniList ID) replacing Sonarr's single TvdbId column
// per locked D-01. Sort + page-size persist via Zustand
// `importListExclusionOptionsStore` (verbatim Sonarr port; localStorage key
// `import_list_exclusion_options`).
//
// Pattern kappa enforcement: data-testid `settings-importlist-exclusions`
// preserved on the wrapper FieldSet content per Phase 26 Plan 26-05; zero
// TV-shape testids permitted on this file.
import React, { useCallback, useEffect, useState } from 'react';
import { SelectProvider, useSelect } from 'App/Select/SelectContext';
import FieldSet from 'Components/FieldSet';
import IconButton from 'Components/Link/IconButton';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageSectionContent from 'Components/Page/PageSectionContent';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TablePager from 'Components/Table/TablePager';
import TableRow from 'Components/Table/TableRow';
import useModalOpenState from 'Helpers/Hooks/useModalOpenState';
import { icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import { CheckInputChanged } from 'typings/inputs';
import {
  registerPagePopulator,
  unregisterPagePopulator,
} from 'Utilities/pagePopulator';
import translate from 'Utilities/String/translate';
import useImportListExclusions, {
  ImportListExclusion,
  useDeleteImportListExclusions,
} from '../useImportListExclusions';
import EditImportListExclusionModal from './EditImportListExclusionModal';
import {
  setImportListExclusionOption,
  setImportListExclusionSort,
  useImportListExclusionOptions,
} from './importListExclusionOptionsStore';
import ImportListExclusionRow from './ImportListExclusionRow';
import styles from './ImportListExclusions.css';

const COLUMNS: Column[] = [
  {
    name: 'title',
    label: () => translate('Title'),
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'mangaDexId',
    label: () => translate('MangaDexId'),
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'malId',
    label: () => translate('MalId'),
    isVisible: true,
    isSortable: true,
  },
  {
    name: 'aniListId',
    label: () => translate('AniListId'),
    isVisible: true,
    isSortable: true,
  },
  {
    className: styles.actions,
    name: 'actions',
    label: '',
    isVisible: true,
    isSortable: false,
  },
];

function ImportListExclusionsContent() {
  const { pageSize, sortKey, sortDirection } = useImportListExclusionOptions();

  const {
    records,
    totalPages,
    totalRecords,
    isFetching,
    isFetched,
    isLoading,
    error,
    page,
    goToPage,
    refetch,
  } = useImportListExclusions({ pageSize, sortKey, sortDirection });

  const { deleteImportListExclusions, isDeleting: isBulkDeleting } =
    useDeleteImportListExclusions();

  const [isConfirmDeleteModalOpen, setIsConfirmDeleteModalOpen] =
    useState(false);

  const [editingExclusionId, setEditingExclusionId] = useState<number | null>(
    null
  );
  const [
    isEditImportListExclusionModalOpen,
    setEditImportListExclusionModalOpen,
    setEditImportListExclusionModalClosed,
  ] = useModalOpenState(false);

  const [
    isAddImportListExclusionModalOpen,
    setAddImportListExclusionModalOpen,
    setAddImportListExclusionModalClosed,
  ] = useModalOpenState(false);

  const {
    allSelected,
    allUnselected,
    anySelected,
    getSelectedIds,
    selectAll,
    unselectAll,
  } = useSelect<ImportListExclusion>();

  const handleSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) => {
      if (value) {
        selectAll();
      } else {
        unselectAll();
      }
    },
    [selectAll, unselectAll]
  );

  const handleSortPress = useCallback(
    (sortKey: string, sortDirection?: SortDirection) => {
      setImportListExclusionSort({ sortKey, sortDirection });
    },
    []
  );

  const handleTableOptionChange = useCallback(
    (payload: { pageSize?: number }) => {
      if (payload.pageSize) {
        setImportListExclusionOption('pageSize', payload.pageSize as number);
        goToPage(1);
      }
    },
    [goToPage]
  );

  const handleDeleteSelectedPress = useCallback(() => {
    setIsConfirmDeleteModalOpen(true);
  }, []);

  const handleDeleteSelectedConfirmed = useCallback(() => {
    // WR-02 (CodeRabbit PR #218): `records` here is the CURRENT PAGE only
    // (paged query slice), so getSelectedIds() reflects user-visible
    // selection scope. Cross-page bulk-delete is the Manage subtree's job
    // (Plan 27.1-04). Mutation onSuccess invalidates the query; do NOT
    // refetch() here — it races the mutate.
    deleteImportListExclusions({ ids: getSelectedIds() });
    setIsConfirmDeleteModalOpen(false);
    unselectAll();
  }, [getSelectedIds, deleteImportListExclusions, unselectAll]);

  const handleConfirmDeleteModalClose = useCallback(() => {
    setIsConfirmDeleteModalOpen(false);
  }, []);

  const handleEditImportListExclusionPress = useCallback(
    (id: number) => {
      setEditingExclusionId(id);
      setEditImportListExclusionModalOpen();
    },
    [setEditImportListExclusionModalOpen]
  );

  const handleEditModalClose = useCallback(() => {
    setEditImportListExclusionModalClosed();
    setEditingExclusionId(null);
    // Refetch on close so any saved edits surface immediately. No mutation
    // in flight at modal-close time (save settled or was cancelled).
    refetch();
  }, [setEditImportListExclusionModalClosed, refetch]);

  const handleAddModalClose = useCallback(() => {
    setAddImportListExclusionModalClosed();
    refetch();
  }, [setAddImportListExclusionModalClosed, refetch]);

  useEffect(() => {
    const repopulate = () => {
      refetch();
    };

    registerPagePopulator(repopulate);

    return () => {
      unregisterPagePopulator(repopulate);
    };
  }, [refetch]);

  const editingExclusion = editingExclusionId
    ? records.find((r) => r.id === editingExclusionId)
    : undefined;

  return (
    <FieldSet legend={translate('ImportListExclusions')}>
      <PageSectionContent
        errorMessage={translate('ImportListExclusionsLoadError')}
        isFetching={isLoading && !isFetched}
        isPopulated={isFetched}
        error={error}
      >
        <div data-testid="settings-importlist-exclusions">
          <Table
            selectAll={true}
            allSelected={allSelected}
            allUnselected={allUnselected}
            columns={COLUMNS}
            canModifyColumns={false}
            pageSize={pageSize}
            sortKey={sortKey}
            sortDirection={sortDirection}
            onTableOptionChange={handleTableOptionChange}
            onSelectAllChange={handleSelectAllChange}
            onSortPress={handleSortPress}
          >
            <TableBody>
              {records.map((item) => {
                return (
                  <ImportListExclusionRow
                    key={item.id}
                    id={item.id}
                    title={item.title}
                    mangaDexId={item.mangaDexId}
                    malId={item.malId}
                    aniListId={item.aniListId}
                    columns={COLUMNS}
                    onEditImportListExclusionPress={
                      handleEditImportListExclusionPress
                    }
                  />
                );
              })}

              <TableRow>
                <TableRowCell colSpan={5}>
                  <SpinnerButton
                    kind={kinds.DANGER}
                    isSpinning={isBulkDeleting}
                    isDisabled={!anySelected}
                    onPress={handleDeleteSelectedPress}
                  >
                    {translate('Delete')}
                  </SpinnerButton>
                </TableRowCell>

                <TableRowCell>
                  <IconButton
                    name={icons.ADD}
                    aria-label={translate('Add')}
                    onPress={setAddImportListExclusionModalOpen}
                  />
                </TableRowCell>
              </TableRow>
            </TableBody>
          </Table>

          <TablePager
            page={page}
            totalPages={totalPages}
            totalRecords={totalRecords}
            isFetching={isFetching}
            onPageSelect={goToPage}
          />
        </div>

        <EditImportListExclusionModal
          isOpen={isAddImportListExclusionModalOpen}
          onModalClose={handleAddModalClose}
        />

        <EditImportListExclusionModal
          id={editingExclusion?.id}
          title={editingExclusion?.title}
          mangaDexId={editingExclusion?.mangaDexId}
          malId={editingExclusion?.malId ?? undefined}
          aniListId={editingExclusion?.aniListId ?? undefined}
          isOpen={isEditImportListExclusionModalOpen}
          onModalClose={handleEditModalClose}
        />

        <ConfirmModal
          isOpen={isConfirmDeleteModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteSelected')}
          message={translate('DeleteSelectedImportListExclusionsMessageText')}
          confirmLabel={translate('DeleteSelected')}
          onConfirm={handleDeleteSelectedConfirmed}
          onCancel={handleConfirmDeleteModalClose}
        />
      </PageSectionContent>
    </FieldSet>
  );
}

function ImportListExclusions() {
  const { pageSize, sortKey, sortDirection } = useImportListExclusionOptions();
  const { records } = useImportListExclusions({
    pageSize,
    sortKey,
    sortDirection,
  });

  return (
    <SelectProvider<ImportListExclusion> items={records}>
      <ImportListExclusionsContent />
    </SelectProvider>
  );
}

export default ImportListExclusions;
