import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import IconButton from 'Components/Link/IconButton';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageSectionContent from 'Components/Page/PageSectionContent';
import useModalOpenState from 'Helpers/Hooks/useModalOpenState';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import useImportListExclusions, {
  ImportListExclusion,
  useDeleteImportListExclusion,
  useDeleteImportListExclusions,
} from '../useImportListExclusions';
import EditImportListExclusionModal from './EditImportListExclusionModal';

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
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const { deleteImportListExclusion, isDeleting } =
    useDeleteImportListExclusion(id);

  const handleEditPress = useCallback(() => setIsEditModalOpen(true), []);

  const handleEditModalClose = useCallback(() => {
    setIsEditModalOpen(false);
    // Edit closes without a backing mutation — refetch immediately so the
    // user sees any pending state cleared (parity with the Indexers/* analog).
    onRefetch();
  }, [onRefetch]);

  const handleDeletePress = useCallback(() => {
    // CodeRabbit PR #218 (WR-02 also from gsd-code-review): the mutation's
    // onSuccess in useDeleteImportListExclusion invalidates the query, which
    // triggers a fresh fetch. Calling onRefetch() here would race the mutate —
    // fire a fetch BEFORE the DELETE settles — and the user would see the
    // pre-delete row return briefly. Drop the manual refetch; rely on the
    // hook's invalidation.
    deleteImportListExclusion();
  }, [deleteImportListExclusion]);

  return (
    <div data-testid={`settings-importlist-exclusion-row-${id}`}>
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
  const { records, isFetching, isFetched, error, refetch } =
    useImportListExclusions();

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
    // Add modal saves through a mutation hook whose onSuccess invalidates
    // the query — the manual refetch is harmless (different fetch source
    // than the mutate races on Delete) but kept for parity with the
    // canonical Sonarr pattern where modal-close-after-edit explicitly
    // refreshes the list. No race risk because no mutation is in flight
    // at modal-close time (save already settled or was cancelled).
    setAddImportListExclusionModalClosed();
    refetch();
  }, [setAddImportListExclusionModalClosed, refetch]);

  const handleDeleteAllPress = useCallback(() => {
    setIsConfirmDeleteModalOpen(true);
  }, []);

  const handleConfirmDeleteAll = useCallback(() => {
    // CodeRabbit PR #218: `records` here is the CURRENT PAGE's slice from the
    // paged query, not the full result set. Bulk-delete across pages requires
    // either a backend `DELETE /api/v5/importlistexclusion?all=true` endpoint
    // OR a fetch-all-then-bulk-delete client roundtrip; both are Phase 27
    // close-out scope (paired with the provider plugins that will populate
    // exclusion volume meaningfully). v1.1 ships current-page bulk delete —
    // honest because the substrate generates zero exclusions until providers
    // sync. Tracked alongside GH #217 if multi-page bulk-delete is needed.
    //
    // refetch() removed per WR-02 — the mutation's onSuccess invalidates the
    // query, which triggers a fresh fetch. Manual refetch races the mutate.
    deleteImportListExclusions({ ids: records.map((r) => r.id) });
    setIsConfirmDeleteModalOpen(false);
  }, [deleteImportListExclusions, records]);

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
          message={translate('DeleteSelectedImportListExclusionsMessageText')}
          confirmLabel={translate('DeleteSelected')}
          onConfirm={handleConfirmDeleteAll}
          onCancel={handleCancelDelete}
        />
      </PageSectionContent>
    </FieldSet>
  );
}

export default ImportListExclusions;
