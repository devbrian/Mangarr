// Phase 27.1 Plan 27.1-03 (IL-EXCLUSIONS) — extracted row component for the
// Sonarr-canonical Exclusions Table rewrite. Replaces the Phase 26 substrate's
// inline `records.map(item => <div>...)` placeholder with a proper
// TableRow + TableSelectCell + 3 ID cells shape. Edit modal lifts to parent
// via `onEditImportListExclusionPress` callback (matches plan D-01 props
// surface); delete is row-owned via the per-row `useDeleteImportListExclusion`
// mutation (Phase 26 behavior preserved); an optional
// `onConfirmDeleteImportListExclusionPress` parent callback can override.
//
// Pitfall 7 enforcement (CRITICAL — see PATTERNS.md):
//   - `mangaDexId || '—'`  — string fallback; both `''` and `null` render as
//     en-dash (Mangarr's API surface for null-MangaDexId rows is empty-string
//     per useImportListExclusions.ts comment).
//   - `malId ?? '—'`      — number fallback via `??`; legal tag-id `0`
//     must render as `0`, not en-dash. Using `||` would mask `0`.
//   - `aniListId ?? '—'`  — number fallback via `??`; same rationale.
//
// Pattern kappa enforcement (Phase 26 Plan 26-05): zero TV-shape testids on
// any new ImportLists file. Row testid uses Mangarr-canonical
// `settings-importlist-exclusion-row-{id}` (preserves Phase 26 selector).
import React, { useCallback } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import useModalOpenState from 'Helpers/Hooks/useModalOpenState';
import { icons, kinds } from 'Helpers/Props';
import { SelectStateInputProps } from 'typings/props';
import translate from 'Utilities/String/translate';
import {
  ImportListExclusion,
  useDeleteImportListExclusion,
} from '../useImportListExclusions';

interface ImportListExclusionRowProps {
  id: number;
  title: string;
  mangaDexId: string;
  malId: number | null;
  aniListId: number | null;
  columns: Column[];
  onEditImportListExclusionPress: (id: number) => void;
  onConfirmDeleteImportListExclusionPress?: (id: number) => void;
}

function ImportListExclusionRow({
  id,
  title,
  mangaDexId,
  malId,
  aniListId,
  onEditImportListExclusionPress,
  onConfirmDeleteImportListExclusionPress,
}: ImportListExclusionRowProps) {
  const { toggleSelected, useIsSelected } = useSelect<ImportListExclusion>();
  const isSelected = useIsSelected(id);

  const { deleteImportListExclusion, isDeleting } =
    useDeleteImportListExclusion(id);

  const handleSelectedChange = useCallback(
    ({ id, value, shiftKey = false }: SelectStateInputProps) => {
      toggleSelected({
        id,
        isSelected: value,
        shiftKey,
      });
    },
    [toggleSelected]
  );

  const [
    isDeleteImportListExclusionModalOpen,
    setDeleteImportListExclusionModalOpen,
    setDeleteImportListExclusionModalClosed,
  ] = useModalOpenState(false);

  const handleEditPress = useCallback(() => {
    onEditImportListExclusionPress(id);
  }, [id, onEditImportListExclusionPress]);

  const handleDeletePress = useCallback(() => {
    if (onConfirmDeleteImportListExclusionPress) {
      // Parent-owned confirmation override (e.g. for bulk-selection flows
      // that want to defer to a shared confirm UX). Skip the row-local modal.
      onConfirmDeleteImportListExclusionPress(id);
      return;
    }
    setDeleteImportListExclusionModalOpen();
  }, [
    id,
    onConfirmDeleteImportListExclusionPress,
    setDeleteImportListExclusionModalOpen,
  ]);

  const handleConfirmDelete = useCallback(() => {
    // CodeRabbit PR #218 / WR-02: the mutation's onSuccess invalidates the
    // query, which triggers a fresh fetch. Do NOT call refetch() here — that
    // races the mutate and can flash the pre-delete row back briefly.
    deleteImportListExclusion();
    setDeleteImportListExclusionModalClosed();
  }, [deleteImportListExclusion, setDeleteImportListExclusionModalClosed]);

  return (
    <TableRow data-testid={`settings-importlist-exclusion-row-${id}`}>
      <TableSelectCell
        id={id}
        isSelected={isSelected}
        onSelectedChange={handleSelectedChange}
      />

      <TableRowCell>{title}</TableRowCell>
      <TableRowCell>{mangaDexId || '—'}</TableRowCell>
      <TableRowCell>{malId ?? '—'}</TableRowCell>
      <TableRowCell>{aniListId ?? '—'}</TableRowCell>

      <TableRowCell>
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
      </TableRowCell>

      <ConfirmModal
        isOpen={isDeleteImportListExclusionModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteImportListExclusion')}
        message={translate('DeleteImportListExclusionMessageText')}
        confirmLabel={translate('Delete')}
        onConfirm={handleConfirmDelete}
        onCancel={setDeleteImportListExclusionModalClosed}
      />
    </TableRow>
  );
}

export default ImportListExclusionRow;
