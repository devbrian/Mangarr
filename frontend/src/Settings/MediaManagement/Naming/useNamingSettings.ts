// Sonarr divergence: Phase 15 Plan 15-12 fix-forward — stub re-export.
// The TV `useNamingSettings` hook + its `/api/v5/settings/naming` endpoint were
// deleted in Plan 15-12 (NamingSettingsController dropped along with Sonarr.Api.V3).
// However, frontend/src/Organize/OrganizePreviewModalContent.tsx still imports
// `useNamingSettings` to drive its TV-shape preview UI. The Organize/ subtree
// is unreachable at runtime (no manga peer route mounts it; Series/* routes
// removed by Plan 15-07's AppRoutes cutover) but the TS module-graph still
// requires the export.
//
// This file is a no-op stub that returns an empty NamingSettingsModel so the
// TS compile passes without the consumer firing any HTTP calls. Phase 8 cleanup:
// delete the Organize/ subtree and this stub together.
import { ApiError } from 'Utilities/Fetch/fetchJson';

export interface NamingSettingsModel {
  renameEpisodes: boolean;
  replaceIllegalCharacters: boolean;
  colonReplacementFormat: number;
  customColonReplacementFormat: string;
  multiEpisodeStyle: number;
  standardEpisodeFormat: string;
  dailyEpisodeFormat: string;
  animeEpisodeFormat: string;
  seriesFolderFormat: string;
  seasonFolderFormat: string;
  specialsFolderFormat: string;
}

export interface NamingExamples {
  singleEpisodeExample: string;
  multiEpisodeExample: string;
  dailyEpisodeExample: string;
  animeEpisodeExample: string;
  animeMultiEpisodeExample: string;
  seriesFolderExample: string;
  seasonFolderExample: string;
  specialsFolderExample: string;
}

const EMPTY_NAMING: NamingSettingsModel = {
  renameEpisodes: false,
  replaceIllegalCharacters: false,
  colonReplacementFormat: 0,
  customColonReplacementFormat: '',
  multiEpisodeStyle: 0,
  standardEpisodeFormat: '',
  dailyEpisodeFormat: '',
  animeEpisodeFormat: '',
  seriesFolderFormat: '',
  seasonFolderFormat: '',
  specialsFolderFormat: '',
};

export const useNamingSettings = () => {
  return {
    data: EMPTY_NAMING,
    isFetching: false,
    isFetched: true,
    error: null as ApiError | null,
  };
};
