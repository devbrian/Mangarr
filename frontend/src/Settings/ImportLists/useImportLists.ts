import { useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
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
// D-05 ENFORCED: NO `useTestAllImportLists` export — Sonarr-canonical Settings
// page has no Test-All / Sync-Now button. Users trigger via System → Tasks UI
// or `POST /api/v5/command {name:"ImportListSync"}`.

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

export const useSortedImportLists = () => {
  const result = useImportLists();

  // CodeRabbit PR #218 CRIT: Array.prototype.sort mutates in place, which
  // would mutate the React Query cached array shared across consumers. Copy
  // first via toSorted() (ES2023) — or slice().sort() for older targets — so
  // the cache stays immutable.
  const sortedData = useMemo(
    () => [...result.data].sort(sortByProp('name')),
    [result.data]
  );

  return {
    ...result,
    data: sortedData,
  };
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

// D-05: No useTestAllImportLists export. Sonarr-canonical Settings page has no
// Test-All button; the inherited /api/v5/importlist/testall endpoint exists on
// the controller but the FE deliberately does not bind it. Users trigger via
// System → Tasks UI or POST /api/v5/command {name:"ImportListSync"}.

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
