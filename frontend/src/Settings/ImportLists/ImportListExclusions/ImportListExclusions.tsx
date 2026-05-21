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
//
// GH-225 (Phase 27.1 REVIEW WR-02) — concurrent-removal guard. When the Edit
// modal is open for `editingExclusionId` and the row vanishes from the paged
// `records` list mid-edit (background refetch, concurrent delete in another
// tab, pagination move), the modal would silently rebind to a blank record
// because the parent passed `id={undefined}` while keeping `isOpen={true}`.
// `useManageImportListExclusion` then falls through to NEW_IMPORT_LIST_EXCLUSION
// and any subsequent Save POSTs a blank-titled exclusion. The
// `concurrentRemovalAlert` effect below detects the disappearance transition
// (modal open + bound id + fetch settled + row gone), closes the modal, and
// surfaces a transient WARNING Alert above the table so the user knows their
// in-flight edit was discarded. The companion backend leg
// (ImportListExclusionController.cs:41 SharedValidator Title.NotEmpty()) is
// already in place and rejects blank-title POST/PUT at the controller layer —
// the FE guard prevents the failed POST from ever being emitted in the first
// place.
import React, { useCallback, useEffect, useState } from 'react';
import { SelectProvider, useSelect } from 'App/Select/SelectContext';
import Alert from 'Components/Alert';
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

// GH-225 — how long the "row vanished mid-edit" Alert stays visible after the
// modal auto-closes. 8s is long enough to read, short enough to not clutter
// the page; matches the rough order-of-magnitude of comparable transient
// banners in the codebase (cf. Activity/Queue/QueueStatus warnings).
const CONCURRENT_REMOVAL_ALERT_TIMEOUT_MS = 8_000;

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

  // GH-225 — visible-banner state for the concurrent-removal guard. Set
  // when the disappearance transition is detected; auto-cleared after a
  // timeout (or on next manual interaction).
  const [isConcurrentRemovalAlertVisible, setIsConcurrentRemovalAlertVisible] =
    useState(false);

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
      // GH-225 — clear any stale concurrent-removal banner when the user
      // begins a fresh edit; the banner is only relevant to the prior
      // interrupted edit.
      setIsConcurrentRemovalAlertVisible(false);
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

  // GH-225 — concurrent-removal guard. When the Edit modal is open with a
  // previously-bound `editingExclusionId` and the row vanishes from records
  // (background refetch + concurrent delete; pagination move; another tab),
  // detect the transition, close the modal, surface a transient WARNING Alert.
  //
  // Gate conditions:
  //   1. Modal must be open (`isEditImportListExclusionModalOpen`).
  //   2. A row must have been bound (`editingExclusionId != null`).
  //   3. Records must be fetched (`isFetched && !isFetching`) so we never
  //      misfire during the initial loading-state window before the first
  //      page lands.
  //   4. The bound id must not be found in the current records page.
  //
  // The effect closes the modal AND clears the editing id; the side-effect of
  // setIsConcurrentRemovalAlertVisible(true) shows the banner. A timeout
  // dismisses the banner after CONCURRENT_REMOVAL_ALERT_TIMEOUT_MS.
  useEffect(() => {
    if (
      isEditImportListExclusionModalOpen &&
      editingExclusionId != null &&
      isFetched &&
      !isFetching &&
      !editingExclusion
    ) {
      setEditImportListExclusionModalClosed();
      setEditingExclusionId(null);
      setIsConcurrentRemovalAlertVisible(true);
    }
  }, [
    isEditImportListExclusionModalOpen,
    editingExclusionId,
    isFetched,
    isFetching,
    editingExclusion,
    setEditImportListExclusionModalClosed,
  ]);

  // GH-225 — banner auto-dismiss timer. Mounted only while visible; cleaned
  // up on un-mount or visibility flip.
  useEffect(() => {
    if (!isConcurrentRemovalAlertVisible) {
      return undefined;
    }

    const timeoutId = window.setTimeout(() => {
      setIsConcurrentRemovalAlertVisible(false);
    }, CONCURRENT_REMOVAL_ALERT_TIMEOUT_MS);

    return () => {
      window.clearTimeout(timeoutId);
    };
  }, [isConcurrentRemovalAlertVisible]);

  return (
    <FieldSet legend={translate('ImportListExclusions')}>
      <PageSectionContent
        errorMessage={translate('ImportListExclusionsLoadError')}
        isFetching={isLoading && !isFetched}
        isPopulated={isFetched}
        error={error}
      >
        <div data-testid="settings-importlist-exclusions">
          {isConcurrentRemovalAlertVisible ? (
            <div data-testid="settings-importlist-exclusion-concurrent-removal-alert">
              <Alert kind={kinds.WARNING}>
                {translate('EditImportListExclusionConcurrentlyRemovedMessage')}
              </Alert>
            </div>
          ) : null}

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
