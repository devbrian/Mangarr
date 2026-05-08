// Sonarr divergence: Phase 15 Plan 15-12 — STUB hooks.
import { useMemo } from 'react';
import Episode from './Episode';

export const useSingleEpisode = (_id?: number): Episode | undefined => undefined;

export const useEpisodesWithIds = (_ids: number[]): Episode[] => {
  return useMemo(() => [], []);
};

export const setEpisodeQueryKey = (_key: string | string[]) => undefined;

export const useToggleEpisodesMonitored = (_keys?: string[]) => ({
  toggleEpisodesMonitored: (_payload: unknown) => undefined,
  isToggling: false,
});

export default useSingleEpisode;
