// Phase 27.1 Plan 27.1-04 — Zustand sort store for ManageImportListsModalContent.
// 1:1 verbatim mirror of frontend/src/Settings/Indexers/useManageIndexersOptionsStore.ts
// per RESEARCH §7 Summary point 7 (Manage subtree is a verbatim peer of the
// Indexers Manage subtree). The only swaps are the localStorage key, the
// interface name, and the hook/setter export names. Defaults unchanged.
import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';
import { SortDirection } from 'Helpers/Props/sortDirections';

export interface ManageImportListsOptions {
  sortKey: string;
  sortDirection: SortDirection;
}

const { useOptions, setSort } = createOptionsStore<ManageImportListsOptions>(
  'manage_import_lists_options',
  () => {
    return {
      sortKey: 'name',
      sortDirection: 'ascending',
    };
  }
);

export const useManageImportListsOptions = useOptions;
export const setManageImportListsSort = setSort;
