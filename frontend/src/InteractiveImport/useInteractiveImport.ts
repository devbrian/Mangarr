// Sonarr divergence: REWRITE per Phase 25 Plan 25-04 Task 4 (v1.1-03 +
// D-04) — see DIVERGENCE.md.
//
// InteractiveImportParams + ReprocessInteractiveImportItem rewritten to
// manga shape: dropped the TV-shape carry-over (series-id / season-number /
// episode-ids); added mangaId / chapterIds / existingFileBehavior. Payload
// path remains /manualimport per D-01 (no manga/ prefix; ManualImport is
// a cross-manga flow, not a per-manga resource).
//
// Defensive `?? []` discipline preserved (P-007) so consumers'
// .length/.find/.reduce calls never crash on empty backend responses.
import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import ModelBase from 'App/ModelBase';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import ExistingFileBehavior from 'typings/ExistingFileBehavior';
import clientSideFilterAndSort from 'Utilities/Filter/clientSideFilterAndSort';
import InteractiveImport from './InteractiveImport';
import { useInteractiveImportOptions } from './interactiveImportOptionsStore';
import ReleaseType from './ReleaseType';

const DEFAULT_ITEMS: InteractiveImport[] = [];

interface InteractiveImportParams {
  downloadIds?: string[];
  mangaId?: number;
  folder?: string;
  filterExistingFiles?: boolean;
}

const useInteractiveImport = (params: InteractiveImportParams) => {
  const { sortKey, sortDirection } = useInteractiveImportOptions();

  const { data, isFetching, isFetched, error, refetch } = useApiQuery<
    InteractiveImport[]
  >({
    path: '/manualimport',
    queryParams: { ...params },
    queryOptions: {
      // Set to 0 so we don't persist the data after the modal is closed and the query becomes inactive
      gcTime: 0,
      // Disable refetch on window focus to prevent refetching when the user switch tabs
      refetchOnWindowFocus: false,
    },
  });

  const items = data ?? DEFAULT_ITEMS;
  const originalItems = [...items];

  const { data: sortedItems } = useMemo(() => {
    const sortPredicates = {
      manga: (item: InteractiveImport) => item.manga?.title || '',
      translatedLanguage: (item: InteractiveImport) =>
        item.translatedLanguage || '',
    };

    return clientSideFilterAndSort(items, {
      sortKey,
      sortDirection,
      sortPredicates,
    });
  }, [items, sortKey, sortDirection]);

  return {
    data: sortedItems,
    originalItems,
    isFetching,
    isFetched,
    error,
    refetch,
  };
};

export default useInteractiveImport;

export const useUpdateInteractiveImportItem = () => {
  const queryClient = useQueryClient();

  const updateInteractiveImportItem = useCallback(
    (id: number, updates: Partial<InteractiveImport>) => {
      queryClient.setQueriesData(
        { queryKey: ['/manualimport'] },
        (oldData: InteractiveImport[] | undefined) => {
          if (!oldData) {
            return oldData;
          }

          return oldData.map((item) => {
            return item.id === id
              ? ({ ...item, ...updates } as InteractiveImport)
              : item;
          });
        }
      );
    },
    [queryClient]
  );

  return { updateInteractiveImportItem };
};

export const useUpdateInteractiveImportItems = () => {
  const queryClient = useQueryClient();

  const updateInteractiveImportItems = useCallback(
    (ids: number[], updates: Partial<InteractiveImport>) => {
      queryClient.setQueriesData(
        { queryKey: ['/manualimport'] },
        (oldData: InteractiveImport[] | undefined) => {
          if (!oldData) {
            return oldData;
          }

          return oldData.map((item) => {
            return ids.includes(item.id)
              ? ({ ...item, ...updates } as InteractiveImport)
              : item;
          });
        }
      );
    },
    [queryClient]
  );

  return { updateInteractiveImportItems };
};

interface ReprocessInteractiveImportItem extends ModelBase {
  path: string;
  relativePath: string;
  mangaId: number | undefined;
  chapterIds: number[] | undefined;
  scanlationGroup: string | undefined;
  translatedLanguage: string | undefined;
  downloadId: string | undefined;
  indexerFlags: number;
  releaseType: ReleaseType;
  existingFileBehavior: ExistingFileBehavior;
}

export const useReprocessInteractiveImportItems = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    InteractiveImport[],
    ReprocessInteractiveImportItem[]
  >({
    path: '/manualimport',
    method: 'POST',
    mutationOptions: {
      onSuccess: (updatedItems) => {
        queryClient.setQueriesData(
          { queryKey: ['/manualimport'] },
          (oldData: InteractiveImport[] | undefined) => {
            if (!oldData) {
              return oldData;
            }

            return oldData.map((oldItem: InteractiveImport) => {
              const reprocessedItem = updatedItems.find(
                (updatedItem) => updatedItem.id === oldItem.id
              );

              return reprocessedItem ? reprocessedItem : oldItem;
            });
          }
        );
      },
    },
  });

  const reprocessInteractiveImportItems = useCallback(
    (ids: number[]) => {
      const [, currentData] = queryClient.getQueriesData<InteractiveImport[]>({
        queryKey: ['/manualimport'],
      })[0];

      if (!currentData) {
        return;
      }

      const requestPayload = ids.reduce<ReprocessInteractiveImportItem[]>(
        (acc, id) => {
          const item = currentData.find((i) => i.id === id);

          if (!item) {
            return acc;
          }

          acc.push({
            id,
            path: item.path,
            relativePath: item.relativePath,
            mangaId: item.manga ? item.manga.id : undefined,
            chapterIds: (item.chapters ?? []).map((c) => c.id),
            scanlationGroup: item.scanlationGroup,
            translatedLanguage: item.translatedLanguage,
            indexerFlags: item.indexerFlags,
            releaseType: item.releaseType,
            downloadId:
              item.kind === 'queue-source' || item.kind === 'manga-imported'
                ? item.downloadId
                : undefined,
            existingFileBehavior:
              item.existingFileBehavior ?? ExistingFileBehavior.Skip,
          });

          return acc;
        },
        []
      );

      mutate(requestPayload);
    },
    [queryClient, mutate]
  );

  return {
    reprocessInteractiveImportItems,
    isReprocessing: isPending,
    error,
  };
};
