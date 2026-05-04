// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/MediaManagement/Naming/useNamingSettings.ts.
//
// Wires Phase 5 Plan 05-06 backend endpoints:
//   * GET  /api/v5/config/manganaming           — manga-shape NamingConfig subset
//   * PUT  /api/v5/config/manganaming           — Save manga fields (preserves TV fields server-side)
//   * GET  /api/v5/config/manganaming/presets/manga — Komga / Kavita / ComicRack / Custom
//
// Manga sibling preserves: useSettings / useManageSettings scaffold + REST PATH constant.
// Manga sibling diverges from useNamingSettings:
//   * Manga-shaped fields (StandardChapterFormat / MangaFolderFormat / RenameChapters) instead of TV fields
//   * Adds useMangaNamingPresets() hook (no /examples endpoint — presets is the manga UX equivalent)
//   * Path is /config/manganaming (compound noun matches MangaNamingConfigController route)
//
// Phase 8 cleanup: this stays — manga-canonical.

import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { useManageSettings, useSettings } from 'Settings/useSettings';

const PATH = '/config/manganaming';
const PRESETS_PATH = '/config/manganaming/presets/manga';

export interface MangaNamingSettingsModel {
  renameChapters: boolean;
  replaceIllegalCharacters: boolean;
  colonReplacementFormat: number;
  customColonReplacementFormat: string;
  standardChapterFormat: string;
  mangaFolderFormat: string;
}

export interface MangaNamingPreset {
  name: string;
  standardChapterFormat: string;
  mangaFolderFormat: string;
  description: string;
}

export const useMangaNamingSettings = () => {
  return useSettings<MangaNamingSettingsModel>(PATH);
};

export const useManageMangaNamingSettings = () => {
  return useManageSettings<MangaNamingSettingsModel>(PATH);
};

export const useMangaNamingPresets = () => {
  const { data, error, isFetching } = useApiQuery<MangaNamingPreset[]>({
    path: PRESETS_PATH,
    method: 'GET',
    queryOptions: {
      gcTime: Infinity,
      staleTime: Infinity,
    },
  });

  return {
    presets: data ?? [],
    presetsError: error,
    isPresetsFetching: isFetching,
  };
};
