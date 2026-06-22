// Phase 42 Plan 42-05 — Discovery persisted view-state store.
// Analog: frontend/src/AddManga/addMangaOptionsStore.ts (exact — createOptionsStore
// + single localStorage key + exported useOptions/useOption/setOption aliases).
//
// localStorage key: 'discovery_options' (verified no collision with
// 'add_manga_options' / 'manga_options' — grep in 42-05-SUMMARY).
// Persists the user's last-used filter selections + the Top-X count (D-09);
// search RESULTS are NOT persisted (they live in volatile React Query state).
import { DiscoveryOptions } from 'Discovery/Discovery';
import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';

const { useOptions, useOption, setOption, setOptions, getOptions } =
  createOptionsStore<DiscoveryOptions>('discovery_options', () => {
    return {
      type: {},
      genre: {},
      status: {},
      contentRating: {},
      tags: [],
      tagMode: 'and',
      includeAdult: false,
      yearLower: undefined,
      yearUpper: undefined,
      ratingLower: undefined,
      ratingUpper: undefined,
      sortBy: 'score_desc',
      topX: 20,
    };
  });

export const useDiscoveryOptions = useOptions;
export const useDiscoveryOption = useOption;
export const setDiscoveryOption = setOption;
export const setDiscoveryOptions = setOptions;
export const getDiscoveryOptions = getOptions;
