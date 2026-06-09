import { useQueryClient } from '@tanstack/react-query';
import { orderBy } from 'lodash';
import { useMemo } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { useManageImportListsOptions } from 'Settings/ImportLists/useManageImportListsOptionsStore';
import {
  SelectedSchema,
  useProviderSchema,
  useSelectedSchema,
} from 'Settings/useProviderSchema';
import {
  useDeleteProvider,
  useManageProviderSettings,
  useProviderSettings,
} from 'Settings/useProviderSettings';
import Provider from 'typings/Provider';
import { sortByProp } from 'Utilities/Array/sortByProp';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import translate from 'Utilities/String/translate';

// Phase 26 Plan 26-05 (IL-05) — TanStack hook for the V5 ImportList substrate
// (`/api/v5/importlist`). Clone of `frontend/src/Settings/Indexers/useIndexers.ts`
// per RESEARCH §Q7 parallel-tree pattern; field set swapped for the ImportList
// provider DTO surface (per `Mangarr.Api.V5.ImportLists.ImportListResource`).
//
// Redux thunk testAllImportLists at Store/Actions/Settings/importLists.js:53
// is the canonical Test All hook per Phase 27.1 D-02; no parallel React Query
// mutation is authored.

export interface ImportListModel extends Provider {
  enableAutomaticAdd: boolean;
  searchForMissingChapters: boolean;
  // Backend `MonitorTypes` / `NewItemMonitorTypes` enums are JSON-serialized as
  // strings ("all" | "existing" | "latest" | "first" | "none"); the
  // MonitorChaptersSelectInput + MonitorNewItemsSelectInput components expect
  // `value: string`. Type the model accordingly.
  shouldMonitor: string;
  monitorNewItems: string;
  rootFolderPath: string;
  translationProfileId: number;
  customFormatProfileId: number;
  listType: string;
  minRefreshInterval: string;
  tags: number[];
}

interface BulkEditImportListsPayload {
  ids: number[];
  [key: string]: unknown;
}

interface BulkDeleteImportListsPayload {
  ids: number[];
}

const PATH = '/importlist';

export const useImportListsWithIds = (ids: number[]) => {
  const allImportLists = useImportListsData();

  return allImportLists.filter((importList) => ids.includes(importList.id));
};

export const useImportList = (id: number | undefined) => {
  const { data } = useImportLists();

  if (id === undefined) {
    return undefined;
  }

  return data.find((importList) => importList.id === id);
};

export const useImportListsData = () => {
  const { data } = useImportLists();

  return data;
};

// Name-sorted view of the import list collection. Consumed by the settings
// page card grid + any other surface that wants the canonical alphabetical
// listing. The Manage Import Lists modal uses `useSortedManageImportLists`
// instead, because that modal binds sort to a user-controlled Zustand store.
export const useSortedImportLists = () => {
  const result = useImportLists();

  // CodeRabbit PR #218 CRIT: Array.prototype.sort mutates in place, which
  // would mutate the React Query cached array shared across consumers. Copy
  // first via spread so the cache stays immutable.
  const sortedData = useMemo(
    () => [...result.data].sort(sortByProp('name')),
    [result.data]
  );

  return {
    ...result,
    data: sortedData,
  };
};

// GH #224 fix (Phase 27.1 27.1-REVIEW WR-01 fix-forward) — the Manage Import
// Lists modal binds its column-header sort to `useManageImportListsOptions()`
// Zustand state (`sortKey` + `sortDirection`). A dedicated hook reads that
// state and applies it via `lodash.orderBy` (the same utility
// `clientSideFilterAndSort` uses), so the visible row order in the Manage
// modal matches what the user picked.
//
// Why a NEW hook and not a tweak to `useSortedImportLists`: the latter is
// also consumed by name-sorted-only surfaces (the settings page card grid,
// future filter dropdowns) that have no UI for changing sort. Keeping the
// hooks separate avoids accidentally coupling those surfaces to the Manage
// modal's persisted Zustand state.
//
// CodeRabbit PR #218 CRIT preserved: `orderBy` returns a new array — the
// React Query cached array referenced by `result.data` is never mutated.
export const useSortedManageImportLists = () => {
  const result = useImportLists();
  const { sortKey, sortDirection } = useManageImportListsOptions();

  const sortedData = useMemo(
    () =>
      orderBy(
        result.data,
        [(item) => normalizeSortValue(item, sortKey)],
        [sortDirection === 'descending' ? 'desc' : 'asc']
      ),
    [result.data, sortKey, sortDirection]
  );

  return {
    ...result,
    data: sortedData,
  };
};

// Normalize a row's sort-value so case-insensitive ordering applies for
// strings (matching what `sortByProp` did pre-fix) and natural numeric
// ordering applies for numbers. Lodash's `orderBy` defaults to JS
// comparison which is case-sensitive for strings.
const normalizeSortValue = (item: ImportListModel, sortKey: string) => {
  const value = (item as unknown as Record<string, unknown>)[sortKey];

  if (value == null) {
    return '';
  }

  if (typeof value === 'string') {
    return value.toLowerCase();
  }

  return value as string | number | boolean;
};

export const useImportLists = () => {
  return useProviderSettings<ImportListModel>({
    path: PATH,
  });
};

export const useManageImportList = (
  id: number | undefined,
  cloneId: number | undefined,
  selectedSchema?: SelectedSchema
) => {
  const schema = useSelectedSchema<ImportListModel>(PATH, selectedSchema);
  const cloneImportList = useImportList(cloneId);

  if (cloneId && !cloneImportList) {
    throw new Error(`ImportList with ID ${cloneId} not found`);
  }

  if (selectedSchema && !schema) {
    throw new Error('A selected schema is required to manage metadata');
  }

  const defaultProvider = useMemo(() => {
    if (cloneId && cloneImportList) {
      const clonedImportList = {
        ...cloneImportList,
        id: 0,
        name: translate('DefaultNameCopiedProfile', {
          name: cloneImportList.name,
        }),
      };

      clonedImportList.fields = clonedImportList.fields.map((field) => {
        const newField = { ...field };

        if (newField.privacy === 'apiKey' || newField.privacy === 'password') {
          newField.value = '';
        }

        return newField;
      });

      return clonedImportList;
    }

    if (selectedSchema && schema) {
      return {
        ...schema,
        name: schema.implementationName,
        // quick-260608-vf9 follow-up — manga-appropriate defaults for a NEW
        // import list (the backend schema otherwise hands back the Sonarr
        // defaults of disabled / monitor-none):
        //   * enableAutomaticAdd: true  — lists exist to add manga
        //   * shouldMonitor: 'all'      — monitor all chapters
        // New-chapter monitoring is no longer a separate field: ImportListSync
        // derives it from this Monitor choice (None => off, else => on), so the
        // single Monitor control governs both existing AND new chapters.
        // customFormatProfileId is left to CustomFormatProfileSelectInput, which
        // self-selects the isDefault profile on mount.
        enableAutomaticAdd: true,
        shouldMonitor: 'all',
      };
    }

    return {} as ImportListModel;
  }, [cloneId, cloneImportList, schema, selectedSchema]);

  const manage = useManageProviderSettings<ImportListModel>(
    id,
    defaultProvider,
    PATH
  );

  return manage;
};

export const useDeleteImportList = (id: number) => {
  const result = useDeleteProvider<ImportListModel>(id, PATH);

  return {
    ...result,
    deleteImportList: result.deleteProvider,
  };
};

export const useImportListSchema = (enabled: boolean = true) => {
  return useProviderSchema<ImportListModel>(PATH, enabled);
};

export const useTestImportList = (
  onSuccess?: () => void,
  onError?: (error: ApiError) => void
) => {
  const { mutate, isPending, error } = useApiMutation<void, ImportListModel>({
    path: `${PATH}/test`,
    method: 'POST',
    mutationOptions: {
      onSuccess,
      onError,
    },
  });

  return {
    testImportList: mutate,
    isTesting: isPending,
    testError: error,
  };
};

// Phase 27.1 D-02: testAllImportLists is the canonical Test-All hook (Redux
// thunk at Store/Actions/Settings/importLists.js:53, wired to the toolbar
// button in ImportListSettings.tsx). The inherited /api/v5/importlist/testall
// endpoint is dispatched via that thunk; no parallel React Query mutation is
// authored here.

export const useBulkEditImportLists = (
  onSuccess?: () => void,
  onError?: (error: ApiError) => void
) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    ImportListModel[],
    BulkEditImportListsPayload
  >({
    path: `${PATH}/bulk`,
    method: 'PUT',
    mutationOptions: {
      onSuccess: (updatedImportLists) => {
        queryClient.setQueryData<ImportListModel[]>(
          [PATH],
          (oldImportLists) => {
            if (!oldImportLists) {
              return oldImportLists;
            }

            return oldImportLists.map((importList) => {
              const updatedImportList = updatedImportLists.find(
                (updated) => updated.id === importList.id
              );

              return updatedImportList
                ? { ...importList, ...updatedImportList }
                : importList;
            });
          }
        );
        onSuccess?.();
      },
      onError,
    },
  });

  return {
    bulkEditImportLists: mutate,
    isSaving: isPending,
    bulkError: error,
  };
};

export const useBulkDeleteImportLists = (
  onSuccess?: () => void,
  onError?: (error: ApiError) => void
) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    void,
    BulkDeleteImportListsPayload
  >({
    path: `${PATH}/bulk`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: (_, variables) => {
        const deletedIds = new Set(variables.ids);

        queryClient.setQueryData<ImportListModel[]>(
          [PATH],
          (oldImportLists) => {
            if (!oldImportLists) {
              return oldImportLists;
            }

            return oldImportLists.filter(
              (importList) => !deletedIds.has(importList.id)
            );
          }
        );
        onSuccess?.();
      },
      onError,
    },
  });

  return {
    bulkDeleteImportLists: mutate,
    isDeleting: isPending,
    bulkDeleteError: error,
  };
};
