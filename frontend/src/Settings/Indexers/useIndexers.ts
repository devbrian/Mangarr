import { useQueryClient } from '@tanstack/react-query';
import { orderBy } from 'lodash';
import { useMemo } from 'react';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { useManageIndexersOptions } from 'Settings/Indexers/useManageIndexersOptionsStore';
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

export interface IndexerModel extends Provider {
  enableRss: boolean;
  enableAutomaticSearch: boolean;
  enableInteractiveSearch: boolean;
  supportsRss: boolean;
  supportsSearch: boolean;
  protocol: DownloadProtocol;
  priority: number;
  downloadClientId: number;
  tags: number[];
}

interface BulkEditIndexersPayload {
  ids: number[];
  [key: string]: unknown;
}

interface BulkDeleteIndexersPayload {
  ids: number[];
}

const PATH = '/indexer';

export const useIndexersWithIds = (ids: number[]) => {
  const allIndexers = useIndexersData();

  return allIndexers.filter((indexer) => ids.includes(indexer.id));
};

export const useIndexer = (id: number | undefined) => {
  const { data } = useIndexers();

  if (id === undefined) {
    return undefined;
  }

  return data.find((indexer) => indexer.id === id);
};

export const useIndexersData = () => {
  const { data } = useIndexers();

  return data;
};

// Name-sorted view of the indexer collection. Consumed by the settings page
// card grid, the `IndexerSelectInput` form dropdown, and the indexer filter
// builder dropdown. The Manage Indexers modal uses `useSortedManageIndexers`
// instead, because that modal binds sort to a user-controlled Zustand store.
//
// GH #224 fix-forward also added a `[...result.data].sort` spread here. The
// prior `result.data.sort(...)` call mutated the React Query cached array
// shared across consumers — the CodeRabbit PR #218 cache-immutability fix
// only landed on the ImportLists peer, never on this Indexers peer. `[...]`
// copies first so the cache stays immutable.
export const useSortedIndexers = () => {
  const result = useIndexers();

  const sortedData = useMemo(
    () => [...result.data].sort(sortByProp('name')),
    [result.data]
  );

  return {
    ...result,
    data: sortedData,
  };
};

// GH #224 fix (Phase 27.1 27.1-REVIEW WR-01 fix-forward) — the Manage
// Indexers modal binds its column-header sort to `useManageIndexersOptions()`
// Zustand state (`sortKey` + `sortDirection`). A dedicated hook reads that
// state and applies it via `lodash.orderBy` (the same utility
// `clientSideFilterAndSort` uses), so the visible row order in the Manage
// modal matches what the user picked.
//
// Why a NEW hook and not a tweak to `useSortedIndexers`: the latter is also
// consumed by name-sorted-only surfaces (the settings page card grid, the
// IndexerSelectInput dropdown, the indexer filter builder dropdown) that
// have no UI for changing sort. Keeping the hooks separate avoids
// accidentally coupling those surfaces to the Manage modal's persisted
// Zustand state.
//
// `orderBy` returns a new array so the React Query cached array referenced
// by `result.data` is never mutated.
export const useSortedManageIndexers = () => {
  const result = useIndexers();
  const { sortKey, sortDirection } = useManageIndexersOptions();

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
const normalizeSortValue = (item: IndexerModel, sortKey: string) => {
  const value = (item as unknown as Record<string, unknown>)[sortKey];

  if (value == null) {
    return '';
  }

  if (typeof value === 'string') {
    return value.toLowerCase();
  }

  return value as string | number | boolean;
};

export const useIndexers = () => {
  return useProviderSettings<IndexerModel>({
    path: PATH,
  });
};

export const useManageIndexer = (
  id: number | undefined,
  cloneId: number | undefined,
  selectedSchema?: SelectedSchema
) => {
  const schema = useSelectedSchema<IndexerModel>(PATH, selectedSchema);
  const cloneIndexer = useIndexer(cloneId);

  if (cloneId && !cloneIndexer) {
    throw new Error(`Indexer with ID ${cloneId} not found`);
  }

  if (selectedSchema && !schema) {
    throw new Error('A selected schema is required to manage metadata');
  }

  const defaultProvider = useMemo(() => {
    if (cloneId && cloneIndexer) {
      const clonedIndexer = {
        ...cloneIndexer,
        id: 0,
        name: translate('DefaultNameCopiedProfile', {
          name: cloneIndexer.name,
        }),
      };

      clonedIndexer.fields = clonedIndexer.fields.map((field) => {
        const newField = { ...field };

        if (newField.privacy === 'apiKey' || newField.privacy === 'password') {
          newField.value = '';
        }

        return newField;
      });

      return clonedIndexer;
    }

    if (selectedSchema && schema) {
      return {
        ...schema,
        name: schema.implementationName,
        enableRss: schema.supportsRss,
        enableAutomaticSearch: schema.supportsSearch,
        enableInteractiveSearch: schema.supportsSearch,
      };
    }

    return {} as IndexerModel;
  }, [cloneId, cloneIndexer, schema, selectedSchema]);

  const manage = useManageProviderSettings<IndexerModel>(
    id,
    defaultProvider,
    PATH
  );

  return manage;
};

export const useDeleteIndexer = (id: number) => {
  const result = useDeleteProvider<IndexerModel>(id, PATH);

  return {
    ...result,
    deleteIndexer: result.deleteProvider,
  };
};

export const useIndexerSchema = (enabled: boolean = true) => {
  return useProviderSchema<IndexerModel>(PATH, enabled);
};

export const useTestIndexer = (
  onSuccess?: () => void,
  onError?: (error: ApiError) => void
) => {
  const { mutate, isPending, error } = useApiMutation<void, IndexerModel>({
    path: `${PATH}/test`,
    method: 'POST',
    mutationOptions: {
      onSuccess,
      onError,
    },
  });

  return {
    testIndexer: mutate,
    isTesting: isPending,
    testError: error,
  };
};

export const useTestAllIndexers = (
  onSuccess?: () => void,
  onError?: (error: ApiError) => void
) => {
  const { mutate, isPending, error } = useApiMutation<void, void>({
    path: `${PATH}/testall`,
    method: 'POST',
    mutationOptions: {
      onSuccess,
      onError,
    },
  });

  return {
    testAllIndexers: mutate,
    isTestingAllIndexers: isPending,
    testAllError: error,
  };
};

export const useBulkEditIndexers = (
  onSuccess?: () => void,
  onError?: (error: ApiError) => void
) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    IndexerModel[],
    BulkEditIndexersPayload
  >({
    path: `${PATH}/bulk`,
    method: 'PUT',
    mutationOptions: {
      onSuccess: (updatedIndexers) => {
        queryClient.setQueryData<IndexerModel[]>([PATH], (oldIndexers) => {
          if (!oldIndexers) {
            return oldIndexers;
          }

          return oldIndexers.map((indexer) => {
            const updatedIndexer = updatedIndexers.find(
              (updated) => updated.id === indexer.id
            );

            return updatedIndexer ? { ...indexer, ...updatedIndexer } : indexer;
          });
        });
        onSuccess?.();
      },
      onError,
    },
  });

  return {
    bulkEditIndexers: mutate,
    isSaving: isPending,
    bulkError: error,
  };
};

export const useBulkDeleteIndexers = (
  onSuccess?: () => void,
  onError?: (error: ApiError) => void
) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    void,
    BulkDeleteIndexersPayload
  >({
    path: `${PATH}/bulk`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: (_, variables) => {
        const deletedIds = new Set(variables.ids);

        queryClient.setQueryData<IndexerModel[]>([PATH], (oldIndexers) => {
          if (!oldIndexers) {
            return oldIndexers;
          }

          return oldIndexers.filter((indexer) => !deletedIds.has(indexer.id));
        });
        onSuccess?.();
      },
      onError,
    },
  });

  return {
    bulkDeleteIndexers: mutate,
    isDeleting: isPending,
    bulkDeleteError: error,
  };
};
