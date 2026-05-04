// Sonarr divergence: NEW manga zustand store per Phase 7 D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/AddSeries/addSeriesOptionsStore.ts (32 lines).
//
// Manga sibling preserves: createOptionsStore pattern, single localStorage key.
// Manga sibling diverges:
//   * 'add_manga_options' localStorage key.
//   * Form fields: rootFolderPath / monitor (5-value MangaMonitor) /
//     translationProfileId (replaces qualityProfileId) / customFormatProfileId (NEW) /
//     searchForMissingChapters (Phase 6 D-06 SearchOnAdd).
//   * No seriesType, no seasonFolder, no searchForCutoffUnmetEpisodes.
//
// Phase 8 cleanup: collapse with addSeriesOptionsStore when AddSeries/ deletes.
import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';
import { MangaMonitor } from 'Manga/Manga';

export interface AddMangaOptions {
  rootFolderPath: string;
  monitor: MangaMonitor;
  translationProfileId: number;
  customFormatProfileId: number;
  searchForMissingChapters: boolean;
  tags: number[];
}

const { useOptions, useOption, setOption } =
  createOptionsStore<AddMangaOptions>('add_manga_options', () => {
    return {
      rootFolderPath: '',
      monitor: 'all',
      translationProfileId: 0,
      customFormatProfileId: 0,
      searchForMissingChapters: false,
      tags: [],
    };
  });

export const useAddMangaOptions = useOptions;
export const useAddMangaOption = useOption;
export const setAddMangaOption = setOption;
