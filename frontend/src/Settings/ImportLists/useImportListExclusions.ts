import { keepPreviousData, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import ModelBase from 'App/ModelBase';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import usePage from 'Helpers/Hooks/usePage';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { usePendingChangesStore } from 'Helpers/Hooks/usePendingChangesStore';
import { SortDirection } from 'Helpers/Props/sortDirections';
import selectSettings from 'Store/Selectors/selectSettings';

// Phase 26 Plan 26-05 (IL-05) — TanStack hook for the V5 ImportListExclusion
// substrate (`/api/v5/importlistexclusion`). Manga-shape triplet replaces
// Sonarr's single TvdbId int per `Mangarr.Api.V5.ImportLists.ImportListExclusionResource`.

export interface ImportListExclusion extends ModelBase {
  // Backend allows NULL MangaDexId for AniList-only / MAL-only exclusions,
  // but the FE form binds `value: string` (no null support in TextInput).
  // Map server-side NULL to empty string for the FE; the controller's
  // existence validator skips NULL/empty per Plan 26-05 spec.
  mangaDexId: string;
  malId: number | null;
  aniListId: number | null;
  title: string;
}

const PATH = '/importlistexclusion';

const NEW_IMPORT_LIST_EXCLUSION: Omit<ImportListExclusion, 'id'> = {
  mangaDexId: '',
  malId: null,
  aniListId: null,
  title: '',
};

interface BulkImportListExclusionData {
  ids: number[];
}

interface UseImportListExclusionsOptions {
  pageSize?: number;
  sortKey?: string;
  sortDirection?: SortDirection;
}

const useImportListExclusions = ({
  pageSize = 10,
  sortKey = 'id',
  sortDirection = 'descending' as SortDirection,
}: UseImportListExclusionsOptions = {}) => {
  const { page, goToPage } = usePage('importListExclusion');

  const { refetch, ...query } = usePagedApiQuery<ImportListExclusion>({
    path: PATH,
    page,
    pageSize,
    sortKey,
    sortDirection,
    queryOptions: {
      placeholderData: keepPreviousData,
    },
  });

  return {
    ...query,
    goToPage,
    page,
    refetch,
  };
};

export default useImportListExclusions;

interface ManageImportListExclusionOptions {
  id?: number;
  title?: string;
  mangaDexId?: string;
  malId?: number | null;
  aniListId?: number | null;
}

export const useManageImportListExclusion = ({
  id,
  title,
  mangaDexId,
  malId,
  aniListId,
}: ManageImportListExclusionOptions) => {
  const queryClient = useQueryClient();

  const item = useMemo(() => {
    if (id) {
      return {
        id,
        title: title ?? '',
        mangaDexId: mangaDexId ?? '',
        malId: malId ?? null,
        aniListId: aniListId ?? null,
      } as ImportListExclusion;
    }

    return { id: 0, ...NEW_IMPORT_LIST_EXCLUSION } as ImportListExclusion;
  }, [id, title, mangaDexId, malId, aniListId]);

  const { pendingChanges, setPendingChange } =
    usePendingChangesStore<ImportListExclusion>({});

  const {
    mutate,
    isPending: isSaving,
    error: saveError,
  } = useApiMutation<ImportListExclusion, ImportListExclusion>({
    path: id ? `${PATH}/${id}` : PATH,
    method: id ? 'PUT' : 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
      },
    },
  });

  const { settings, validationErrors, validationWarnings } = useMemo(() => {
    return selectSettings(item, pendingChanges, saveError);
  }, [item, pendingChanges, saveError]);

  const updateValue = useCallback(
    (name: string, value: unknown) => {
      // @ts-expect-error - name is not yet typed as keyof ImportListExclusion
      setPendingChange(name, value);
    },
    [setPendingChange]
  );

  const save = useCallback(() => {
    const payload = {
      ...item,
      ...pendingChanges,
    } as ImportListExclusion;

    if (id) {
      payload.id = id;
    }

    mutate(payload);
  }, [id, item, pendingChanges, mutate]);

  return {
    item: settings,
    isSaving,
    saveError,
    validationErrors,
    validationWarnings,
    updateValue,
    save,
  };
};

export const useDeleteImportListExclusion = (id: number) => {
  const queryClient = useQueryClient();

  const { mutate, isPending } = useApiMutation<void, void>({
    path: `${PATH}/${id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
      },
    },
  });

  return {
    deleteImportListExclusion: mutate,
    isDeleting: isPending,
  };
};

export const useDeleteImportListExclusions = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending } = useApiMutation<
    void,
    BulkImportListExclusionData
  >({
    path: `${PATH}/bulk`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
      },
    },
  });

  return {
    deleteImportListExclusions: mutate,
    isDeleting: isPending,
  };
};
