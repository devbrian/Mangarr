// Phase 27.1 Plan 27.1-04 Task 3 — Manage subtree content. 1:1 mirror of
// frontend/src/Settings/Indexers/Indexers/Manage/ManageIndexersModalContent.tsx
// per PATTERNS §Manage Subtree substitution table.
//
// COLUMNS swap: drop Indexer protocol / enableRss / enableAutomaticSearch /
// enableInteractiveSearch / priority columns; add manga-shape enableAutomaticAdd,
// rootFolderPath, translationProfileId columns. Other shape (SelectProvider +
// inner/outer pattern + bulk-edit/bulk-delete/Tags dispatch) is unchanged from
// the analog. Pattern kappa preserved (zero TV-shape tokens).
//
// GH #224 fix-forward (Phase 27.1 27.1-REVIEW WR-01) — the outer
// `<SelectProvider items={...}>` now receives the SORTED array (from
// `useSortedManageImportLists()`) so `useSelect.getToggledRange()` walks the
// same array the user sees. The pre-fix shape passed `useImportListsData()`
// (raw React Query cache, unsorted) which caused shift-click range-select to
// highlight the wrong contiguous block. The dedicated
// `useSortedManageImportLists` hook (vs. `useSortedImportLists`) is bound to
// the Manage modal's Zustand sort store so column-header clicks actually
// re-sort the visible rows.
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { SelectProvider, useSelect } from 'App/Select/SelectContext';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { kinds } from 'Helpers/Props';
import {
  ImportListModel,
  useBulkDeleteImportLists,
  useBulkEditImportLists,
  useSortedManageImportLists,
} from 'Settings/ImportLists/useImportLists';
import {
  setManageImportListsSort,
  useManageImportListsOptions,
} from 'Settings/ImportLists/useManageImportListsOptionsStore';
import { CheckInputChanged } from 'typings/inputs';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import ManageImportListsEditModal from './Edit/ManageImportListsEditModal';
import ManageImportListsModalRow from './ManageImportListsModalRow';
import TagsModal from './Tags/TagsModal';
import styles from './ManageImportListsModalContent.css';

const COLUMNS: Column[] = [
  {
    name: 'name',
    label: () => translate('Name'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'implementation',
    label: () => translate('Implementation'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'enableAutomaticAdd',
    label: () => translate('AutomaticAdd'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'rootFolderPath',
    label: () => translate('RootFolder'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'translationProfileId',
    label: () => translate('TranslationProfile'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'tags',
    label: () => translate('Tags'),
    isSortable: true,
    isVisible: true,
  },
];

interface ManageImportListsModalContentProps {
  onModalClose(): void;
}

interface ManageImportListsModalContentInnerProps {
  onModalClose(): void;
  data: ImportListModel[];
  isFetching: boolean;
  isFetched: boolean;
  error: ApiError | null;
}

function ManageImportListsModalContentInner(
  props: ManageImportListsModalContentInnerProps
) {
  const { onModalClose, data, isFetching, isFetched, error } = props;

  const { sortKey, sortDirection } = useManageImportListsOptions();

  const { isDeleting, bulkDeleteImportLists } = useBulkDeleteImportLists();
  const { isSaving, bulkEditImportLists } = useBulkEditImportLists();

  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isTagsModalOpen, setIsTagsModalOpen] = useState(false);
  const [isSavingTags, setIsSavingTags] = useState(false);

  // Phase 27.1 27.1-REVIEW post-ship CodeRabbit P3 fix-forward (2026-05-21):
  // `isSavingTags` is set to `true` when a Tags save starts (line 164 onward)
  // but was never reset when the underlying `useBulkEditImportLists` mutation
  // settled. Result: any subsequent non-Tags Edit save would surface the Tags
  // spinner state (`isSaving && isSavingTags` at line 252) even though no Tags
  // operation was pending. Reset on the trailing edge of `isSaving` — when the
  // mutation flips from true back to false — so the next save starts clean.
  const wasSavingRef = useRef(false);
  useEffect(() => {
    if (wasSavingRef.current && !isSaving) {
      setIsSavingTags(false);
    }
    wasSavingRef.current = isSaving;
  }, [isSaving]);

  const {
    allSelected,
    allUnselected,
    anySelected,
    getSelectedIds,
    selectAll,
    unselectAll,
    useSelectedIds,
  } = useSelect<ImportListModel>();

  const onSortPress = useCallback((value: string) => {
    setManageImportListsSort({ sortKey: value });
  }, []);

  const onDeletePress = useCallback(() => {
    setIsDeleteModalOpen(true);
  }, [setIsDeleteModalOpen]);

  const onDeleteModalClose = useCallback(() => {
    setIsDeleteModalOpen(false);
  }, [setIsDeleteModalOpen]);

  const onEditPress = useCallback(() => {
    setIsEditModalOpen(true);
  }, [setIsEditModalOpen]);

  const onEditModalClose = useCallback(() => {
    setIsEditModalOpen(false);
  }, [setIsEditModalOpen]);

  const onConfirmDelete = useCallback(() => {
    bulkDeleteImportLists({ ids: getSelectedIds() });
    setIsDeleteModalOpen(false);
  }, [bulkDeleteImportLists, getSelectedIds]);

  const onSavePress = useCallback(
    (payload: object) => {
      setIsEditModalOpen(false);

      bulkEditImportLists({
        ids: getSelectedIds(),
        ...payload,
      });
    },
    [getSelectedIds, bulkEditImportLists]
  );

  const onTagsPress = useCallback(() => {
    setIsTagsModalOpen(true);
  }, [setIsTagsModalOpen]);

  const onTagsModalClose = useCallback(() => {
    setIsTagsModalOpen(false);
  }, [setIsTagsModalOpen]);

  const onApplyTagsPress = useCallback(
    (tags: number[], applyTags: string) => {
      setIsSavingTags(true);
      setIsTagsModalOpen(false);

      bulkEditImportLists({
        ids: getSelectedIds(),
        tags,
        applyTags,
      });
    },
    [getSelectedIds, bulkEditImportLists]
  );

  const onSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) => {
      if (value) {
        selectAll();
      } else {
        unselectAll();
      }
    },
    [selectAll, unselectAll]
  );

  const selectedIds = useSelectedIds();
  const errorMessage = getErrorMessage(error, 'Unable to load import lists.');

  return (
    <ModalContent
      data-testid="manage-importlists-modal-content"
      onModalClose={onModalClose}
    >
      <ModalHeader>{translate('ManageImportLists')}</ModalHeader>
      <ModalBody>
        {isFetching ? <LoadingIndicator /> : null}

        {error ? <div>{errorMessage}</div> : null}

        {isFetched && !error && !data.length ? (
          <Alert kind={kinds.INFO}>{translate('NoImportListsFound')}</Alert>
        ) : null}

        {isFetched && !!data.length && !isFetching ? (
          <Table
            columns={COLUMNS}
            horizontalScroll={true}
            selectAll={true}
            allSelected={allSelected}
            allUnselected={allUnselected}
            sortKey={sortKey}
            sortDirection={sortDirection}
            onSelectAllChange={onSelectAllChange}
            onSortPress={onSortPress}
          >
            <TableBody>
              {data.map((item) => {
                return (
                  <ManageImportListsModalRow
                    key={item.id}
                    {...item}
                    columns={COLUMNS}
                  />
                );
              })}
            </TableBody>
          </Table>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <div className={styles.leftButtons}>
          <SpinnerButton
            kind={kinds.DANGER}
            isSpinning={isDeleting}
            isDisabled={!anySelected}
            onPress={onDeletePress}
          >
            {translate('Delete')}
          </SpinnerButton>

          <SpinnerButton
            isSpinning={isSaving}
            isDisabled={!anySelected}
            onPress={onEditPress}
          >
            {translate('Edit')}
          </SpinnerButton>

          <SpinnerButton
            isSpinning={isSaving && isSavingTags}
            isDisabled={!anySelected}
            onPress={onTagsPress}
          >
            {translate('SetTags')}
          </SpinnerButton>
        </div>

        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>

      <ManageImportListsEditModal
        isOpen={isEditModalOpen}
        importListIds={selectedIds}
        onModalClose={onEditModalClose}
        onSavePress={onSavePress}
      />

      <TagsModal
        isOpen={isTagsModalOpen}
        ids={selectedIds}
        onApplyTagsPress={onApplyTagsPress}
        onModalClose={onTagsModalClose}
      />

      <ConfirmModal
        isOpen={isDeleteModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteSelectedImportLists')}
        message={translate('DeleteSelectedImportListsMessageText', {
          count: selectedIds.length,
        })}
        confirmLabel={translate('Delete')}
        onConfirm={onConfirmDelete}
        onCancel={onDeleteModalClose}
      />
    </ModalContent>
  );
}

function ManageImportListsModalContent(
  props: ManageImportListsModalContentProps
) {
  // GH #224 fix-forward — read the SORTED list once at the wrapper level and
  // pass it both to <SelectProvider items={...}> AND down to the inner
  // component as `data`. This guarantees `useSelect.getToggledRange()` and
  // the rendered <Table> walk the identical array, so shift-click range-
  // select highlights the contiguous block the user actually sees.
  const { data, isFetching, isFetched, error } = useSortedManageImportLists();

  return (
    <SelectProvider items={data}>
      <ManageImportListsModalContentInner
        {...props}
        data={data}
        isFetching={isFetching}
        isFetched={isFetched}
        error={error}
      />
    </SelectProvider>
  );
}

export default ManageImportListsModalContent;
