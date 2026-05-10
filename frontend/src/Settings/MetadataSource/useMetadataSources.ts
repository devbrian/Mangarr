import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
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
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

// Mirrors NotificationModel shape (Settings/Notifications/useConnections.ts) — the closest
// canonical analog. MetadataSource has no per-event toggles or protocol/priority/RSS axes;
// it adds a single `isPrimary` bool surfaced by Phase 2 backend D-15. The at-most-one
// invariant is enforced server-side inside MetadataSourceFactory.SetPrimary — the UI hits
// POST /api/v5/metadatasource/{id}/setprimary which atomically demotes all others.
export interface MetadataSourceModel extends Provider {
  isPrimary: boolean;
  tags: number[];
}

const PATH = '/metadatasource';

export const useMetadataSource = (id: number | undefined) => {
  const { data } = useMetadataSources();

  if (id === undefined) {
    return undefined;
  }

  return data.find((source) => source.id === id);
};

export const useMetadataSourcesData = () => {
  const { data } = useMetadataSources();

  return data;
};

export const useSortedMetadataSources = () => {
  const { data } = useMetadataSources();

  return useMemo(() => data.slice().sort(sortByProp('name')), [data]);
};

export const useMetadataSources = () => {
  return useProviderSettings<MetadataSourceModel>({
    path: PATH,
  });
};

export const useManageMetadataSource = (
  id: number | undefined,
  selectedSchema?: SelectedSchema
) => {
  const schema = useSelectedSchema<MetadataSourceModel>(PATH, selectedSchema);

  if (selectedSchema && !schema) {
    throw new Error(
      'A selected schema is required to manage a metadata source'
    );
  }

  const manage = useManageProviderSettings<MetadataSourceModel>(
    id,
    selectedSchema && schema
      ? ({
          ...schema,
          name: schema.implementationName || '',
          // D-15 invariant: never default a new source to primary. Promotion is an
          // explicit user action via SetPrimary mutation, never a side-effect of Add.
          isPrimary: false,
        } as MetadataSourceModel)
      : ({} as MetadataSourceModel),
    PATH
  );

  return manage;
};

export const useDeleteMetadataSource = (id: number) => {
  const result = useDeleteProvider<MetadataSourceModel>(id, PATH);

  return {
    ...result,
    deleteMetadataSource: result.deleteProvider,
  };
};

export const useMetadataSourceSchema = (enabled: boolean = true) => {
  return useProviderSchema<MetadataSourceModel>(PATH, enabled);
};

// D-15 SetPrimary — POST /api/v5/metadatasource/{id}/setprimary atomically demotes all
// other sources and promotes the target. The route handler delegates to
// MetadataSourceFactory.SetPrimary which enforces the at-most-one invariant server-side.
// On success we optimistically update every cached row's isPrimary flag so the UI flips
// without a refetch (mirrors the Sonarr cache-write pattern in useBulkEditIndexers).
//
// `defaultId` is the id captured at hook-creation time (used by the card-level
// "Set as Primary" button which knows its row id). The returned `setPrimary`
// also accepts an `idOverride` argument so the Add-Source flow can promote a
// just-created row whose id was unknown at hook-creation time (looked up by
// name from the cache after save). useApiMutation's path is fixed at hook
// construction, so we use react-query's useMutation directly here to make the
// path id-flexible.
export const useSetPrimaryMetadataSource = (
  defaultId: number,
  onSuccess?: () => void,
  onError?: (error: ApiError) => void
) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useMutation<void, ApiError, number | void>({
    mutationFn: async (idArg) => {
      const id = typeof idArg === 'number' ? idArg : defaultId;

      if (!id) {
        throw new Error('SetPrimaryMetadataSource called with no id');
      }

      return fetchJson<void, void>({
        path: `${getQueryPath(PATH)}/${id}/setprimary`,
        method: 'POST',
        headers: {
          'X-Api-Key': window.Mangarr.apiKey,
          'X-Mangarr-Client': 'Mangarr',
        },
      });
    },
    onSuccess: (_data, idArg) => {
      const id = typeof idArg === 'number' ? idArg : defaultId;

      queryClient.setQueryData<MetadataSourceModel[]>(
        [PATH],
        (oldData = []) =>
          oldData.map((source) => ({
            ...source,
            isPrimary: source.id === id,
          }))
      );
      onSuccess?.();
    },
    onError,
  });

  return {
    setPrimary: mutate,
    isSettingPrimary: isPending,
    setPrimaryError: error,
  };
};
