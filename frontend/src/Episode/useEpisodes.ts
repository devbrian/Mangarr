// Sonarr divergence: Phase 15 Plan 15-12 — STUB.
import { useMemo } from 'react';
import Episode from './Episode';

const useEpisodes = (..._args: unknown[]) => {
  return useMemo(() => ({
    data: [] as Episode[],
    isFetching: false,
    isFetched: true,
    error: null,
    refetch: () => undefined,
  }), []);
};

export default useEpisodes;
