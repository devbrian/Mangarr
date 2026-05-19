// Sonarr divergence: NEW manga sibling per Phase 7 D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/AddSeries/AddNewSeries/useAddSeries.ts.
//
// Manga sibling preserves: useApiQuery + useApiMutation pattern; debounced
// lookup; query-key contract.
// Manga sibling diverges from useAddSeries:
//   * Lookup hits /api/v5/manga/lookup (NOT /api/v3/series/lookup or
//     /api/v5/series/lookup).
//   * Mutation POSTs to /api/v5/manga (Phase 2 MangaController.cs:88-111).
//   * SignalR cache invalidation key '/manga' (Plan 07-02 contract — manga
//     handler in SignalRListener.tsx invalidates ['/manga']).
//
// Phase 8 cleanup: collapse with useAddSeries when AddSeries/ deletes.
import { useQueryClient } from '@tanstack/react-query';
import { AddMangaPayload, AddMangaResult } from 'AddManga/AddManga';
import useApiMutation, {
  addOrUpdateQueryClientItem,
} from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Manga from 'Manga/Manga';

const DEFAULT_MANGA: AddMangaResult[] = [];

export const useLookupManga = (query: string, isEnabled = true) => {
  const result = useApiQuery<AddMangaResult[]>({
    path: '/manga/lookup',
    queryParams: {
      term: query,
    },
    queryOptions: {
      enabled: isEnabled && !!query,
      // Disable refetch on window focus to prevent refetching when the user
      // switches tabs.
      refetchOnWindowFocus: false,
    },
  });

  return {
    ...result,
    data: result.data ?? DEFAULT_MANGA,
  };
};

export const useAddManga = () => {
  const queryClient = useQueryClient();

  // Expose both `mutate` (fire-and-forget) and `mutateAsync` (Promise-returning)
  // so multi-row import flows (ImportMangaFooter) can `Promise.allSettled` over
  // every row before redirecting — the single `isPending` boolean flips false
  // on the first settlement, which is too coarse for a multi-row settlement
  // gate. Single-row callers (AddNewMangaModalContent) keep using `mutate` +
  // `isAdding`.
  const { isPending, error, mutate, mutateAsync } = useApiMutation<
    Manga,
    AddMangaPayload
  >({
    path: '/manga',
    method: 'POST',
    mutationOptions: {
      onSuccess: (newManga) => {
        queryClient.setQueryData<Manga[]>(['/manga'], (oldManga = []) =>
          addOrUpdateQueryClientItem(oldManga, newManga, 'id')
        );
      },
    },
  });

  return {
    isAdding: isPending,
    addError: error,
    addManga: mutate,
    addMangaAsync: mutateAsync,
  };
};
