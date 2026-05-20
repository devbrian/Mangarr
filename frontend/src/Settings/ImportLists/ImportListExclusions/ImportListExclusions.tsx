import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import IconButton from 'Components/Link/IconButton';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageSectionContent from 'Components/Page/PageSectionContent';
import useModalOpenState from 'Helpers/Hooks/useModalOpenState';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import EditImportListExclusionModal from './EditImportListExclusionModal';
import useImportListExclusions, {
  ImportListExclusion,
  useDeleteImportListExclusion,
  useDeleteImportListExclusions,
} from '../useImportListExclusions';

// Phase 26 Plan 26-05 (IL-05) — ImportListExclusion paged list. Trimmed mirror
// of Sonarr-ref `frontend-settings/ImportListExclusions/ImportListExclusions.tsx`
// per RESEARCH §Q7 — drops the bulk-select / SelectProvider / column-sort
// machinery to stay within Plan 26-05's file budget (the substrate needs only
// CRUD + Add + Delete; bulk-delete remains on the controller for Phase 27
// expansion). MangaDexId triplet replaces TvdbId per Migration 003.

interface ImportListExclusionRowProps extends ImportListExclusion {
  onRefetch: () => void;
}

function ImportListExclusionRow({
  id,
  title,
  mangaDexId,
  malId,
  aniListId,
  onRefetch,
}: ImportListExclusionRowProps) {
  const [isEditModalOpen, setEditModalOpen] = useState(false);
  const { deleteImportListExclusion, isDeleting } =
    useDeleteImportListExclusion(id);

  const handleEditPress = useCallback(() => setEditModalOpen(true), []);

  const handleEditModalClose = useCallback(() => {
    setEditModalOpen(false);
    onRefetch();
  }, [onRefetch]);

  const handleDeletePress = useCallback(() => {
    deleteImportListExclusion();
    onRefetch();
  }, [deleteImportListExclusion, onRefetch]);

  return (
    <div data-testid={`importlist-exclusion-row-${id}`}>
      <span>{title}</span>
      <span>{mangaDexId || '—'}</span>
      <span>{malId ?? '—'}</span>
      <span>{aniListId ?? '—'}</span>
      <IconButton
        name={icons.EDIT}
        aria-label={translate('Edit')}
        onPress={handleEditPress}
      />
      <IconButton
        name={icons.REMOVE}
        aria-label={translate('Delete')}
        isDisabled={isDeleting}
        onPress={handleDeletePress}
      />
      <EditImportListExclusionModal
        id={id}
        title={title}
        mangaDexId={mangaDexId}
        malId={malId ?? undefined}
        aniListId={aniListId ?? undefined}
        isOpen={isEditModalOpen}
        onModalClose={handleEditModalClose}
      />
    </div>
  );
}

function ImportListExclusions() {
  const {
    records,
    isFetching,
    isFetched,
    error,
    refetch,
  } = useImportListExclusions();

  const { deleteImportListExclusions, isDeleting: isBulkDeleting } =
    useDeleteImportListExclusions();

  const [
    isAddImportListExclusionModalOpen,
    setAddImportListExclusionModalOpen,
    setAddImportListExclusionModalClosed,
  ] = useModalOpenState(false);

  const [isConfirmDeleteModalOpen, setIsConfirmDeleteModalOpen] =
    useState(false);

  const handleAddModalClose = useCallback(() => {
    setAddImportListExclusionModalClosed();
    refetch();
  }, [setAddImportListExclusionModalClosed, refetch]);

  const handleDeleteAllPress = useCallback(() => {
    setIsConfirmDeleteModalOpen(true);
  }, []);

  const handleConfirmDeleteAll = useCallback(() => {
    deleteImportListExclusions({ ids: records.map((r) => r.id) });
    setIsConfirmDeleteModalOpen(false);
    refetch();
  }, [deleteImportListExclusions, records, refetch]);

  const handleCancelDelete = useCallback(() => {
    setIsConfirmDeleteModalOpen(false);
  }, []);

  return (
    <FieldSet legend={translate('ImportListExclusions')}>
      <PageSectionContent
        errorMessage={translate('ImportListExclusionsLoadError')}
        isFetching={isFetching && !isFetched}
        isPopulated={isFetched}
        error={error}
      >
        <div data-testid="settings-importlist-exclusions">
          {records.map((item) => (
            <ImportListExclusionRow
              key={item.id}
              {...item}
              onRefetch={refetch}
            />
          ))}
        </div>

        <div>
          <SpinnerButton
            kind={kinds.DANGER}
            isSpinning={isBulkDeleting}
            isDisabled={records.length === 0}
            onPress={handleDeleteAllPress}
          >
            {translate('Delete')}
          </SpinnerButton>

          <IconButton
            name={icons.ADD}
            aria-label={translate('Add')}
            onPress={setAddImportListExclusionModalOpen}
          />
        </div>

        <EditImportListExclusionModal
          isOpen={isAddImportListExclusionModalOpen}
          onModalClose={handleAddModalClose}
        />

        <ConfirmModal
          isOpen={isConfirmDeleteModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteSelected')}
          message={translate(
            'DeleteSelectedImportListExclusionsMessageText'
          )}
          confirmLabel={translate('DeleteSelected')}
          onConfirm={handleConfirmDeleteAll}
          onCancel={handleCancelDelete}
        />
      </PageSectionContent>
    </FieldSet>
  );
}

export default ImportListExclusions;
