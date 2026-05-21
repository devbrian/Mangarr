// Phase 27.1 Plan 27.1-03 (IL-EXCLUSIONS) — verbatim port of Sonarr
// v5-develop's `importListExclusionOptionsStore.ts`. Zustand-backed
// pageSize/sortKey/sortDirection persister; localStorage key
// `import_list_exclusion_options` mirrors Sonarr-canonical. Default sort is
// `id descending` per Sonarr (matches Mangarr's existing `usePagedApiQuery`
// hook default for `useImportListExclusions`).
import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';
import { SortDirection } from 'Helpers/Props/sortDirections';

export interface ImportListExclusionOptions {
  pageSize: number;
  sortKey: string;
  sortDirection: SortDirection;
}

const { useOptions, setOptions, setOption, setSort } =
  createOptionsStore<ImportListExclusionOptions>(
    'import_list_exclusion_options',
    () => ({
      pageSize: 20,
      sortKey: 'id',
      sortDirection: 'descending',
    })
  );

export const useImportListExclusionOptions = useOptions;
export const setImportListExclusionOptions = setOptions;
export const setImportListExclusionOption = setOption;
export const setImportListExclusionSort = setSort;
