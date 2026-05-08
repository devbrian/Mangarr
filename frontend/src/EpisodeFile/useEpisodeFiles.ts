// Sonarr divergence: Phase 15 Plan 15-12 — STUB hooks.
import { useMemo } from 'react';
const useEpisodeFiles = (..._args: unknown[]) => {
  return useMemo(() => ({ data: [], isFetching: false, isFetched: true, error: null, refetch: () => undefined }), []);
};
export const useDeleteEpisodeFiles = (..._args: unknown[]) => ({
  deleteEpisodeFiles: (..._a: unknown[]) => undefined,
  isDeleting: false,
  deleteError: null as unknown,
});
export const useUpdateEpisodeFiles = (..._args: unknown[]) => ({
  updateEpisodeFiles: (..._a: unknown[]) => undefined,
  isUpdating: false,
});
export default useEpisodeFiles;
