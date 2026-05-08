// Sonarr divergence: Phase 15 Plan 15-12 — STUB hooks.
import { useMemo } from 'react';
import Episode from './Episode';

export const useSingleEpisode = (..._args: unknown[]): Episode | undefined => undefined;

export const useEpisodesWithIds = (..._args: unknown[]): Episode[] => {
  return useMemo(() => [], []);
};

export const setEpisodeQueryKey = (..._args: unknown[]) => undefined;

export const useToggleEpisodesMonitored = (..._args: unknown[]) => ({
  toggleEpisodesMonitored: (_payload: unknown) => undefined,
  isToggling: false,
});

export default useSingleEpisode;
