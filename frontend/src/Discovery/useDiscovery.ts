// Phase 42 Plan 42-05 — Discovery React Query hooks.
// Analog: frontend/src/AddManga/AddNewManga/useAddManga.ts (exact — useApiQuery
// for cached GETs + useApiMutation for the POSTs).
//
// D-03 (no MangaBaka call until Search): useDiscoverySearch is a MUTATION, so the
// /discovery/search request fires ONLY when the Search button invokes mutate() —
// never on mount. This satisfies threat T-42-05-RATE (no auto-fetch burning the
// MangaBaka rate budget). Results are returned to the caller and held in
// component state (stable until the next Search); they are NOT persisted (D-09).
//
// useDiscoveryGenres / useDiscoveryTags are plain cached GETs (staleTime:Infinity
// — the genre/tag vocabularies are effectively static for the session).
import {
  DiscoveryBulkAddPayload,
  DiscoveryGenre,
  DiscoverySearchRequest,
  DiscoverySearchResponse,
  DiscoveryTag,
} from 'Discovery/Discovery';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

const DEFAULT_GENRES: DiscoveryGenre[] = [];
const DEFAULT_TAGS: DiscoveryTag[] = [];

export const useDiscoveryGenres = () => {
  const result = useApiQuery<DiscoveryGenre[]>({
    path: '/discovery/genres',
    queryOptions: {
      staleTime: Infinity,
      refetchOnWindowFocus: false,
    },
  });

  return {
    ...result,
    data: result.data ?? DEFAULT_GENRES,
  };
};

export const useDiscoveryTags = () => {
  const result = useApiQuery<DiscoveryTag[]>({
    path: '/discovery/tags',
    queryOptions: {
      staleTime: Infinity,
      refetchOnWindowFocus: false,
    },
  });

  return {
    ...result,
    data: result.data ?? DEFAULT_TAGS,
  };
};

// Manual-trigger Search (D-03). The mutation does not fire until mutate() is
// called by the Search button — there is NO auto-fetch on mount.
export const useDiscoverySearch = () => {
  return useApiMutation<DiscoverySearchResponse, DiscoverySearchRequest>({
    path: '/discovery/search',
    method: 'POST',
  });
};

// Fire-and-forget bulk add (D-07). The backend enqueues a DiscoveryBulkAddCommand
// and returns 202; library updates arrive via the existing `manga` SignalR fan-out.
export const useDiscoveryBulkAdd = () => {
  return useApiMutation<unknown, DiscoveryBulkAddPayload>({
    path: '/discovery/bulk-add',
    method: 'POST',
  });
};
