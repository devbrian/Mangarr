// Sonarr divergence: NEW manga zustand store per Phase 7 D-02 / Lock #2 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/seriesOptionsStore.ts (302 lines).
//
// Manga sibling preserves: 3-view toggle (posters / overview / table), Posters /
// Overview / Table options blocks, columns array, sort/filter keys, deleteOptions.
//
// Manga sibling diverges from seriesOptionsStore:
//   * 'manga_options' localStorage key (instead of 'series_options').
//   * Column set replaces Season / Episode / Quality with TranslationProfile /
//     CustomFormatProfile / ScanlationGroup / TranslatedLanguage axes
//     (Plan 07-04 fills the canonical column list).
//   * showQualityProfile → showTranslationProfile in posterOptions / overviewOptions.
//   * Default view: 'posters' (Lock #2).
//
// Phase 8 cleanup: collapse with seriesOptionsStore when Series/ deletes.
import Column from 'Components/Table/Column';
import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';

export interface MangaOptions {
  selectedFilterKey: string | number;
  sortKey: string;
  sortDirection: 'ascending' | 'descending';
  view: string;
  columns: Column[];
  posterOptions: {
    detailedProgressBar: boolean;
    size: 'small' | 'medium' | 'large';
    showTitle: boolean;
    showMonitored: boolean;
    showTranslationProfile: boolean;
    showCustomFormatProfile: boolean;
    showTags: boolean;
    showSearchAction: boolean;
  };
  overviewOptions: {
    detailedProgressBar: boolean;
    size: 'small' | 'medium' | 'large';
    showMonitored: boolean;
    showTranslationProfile: boolean;
    showCustomFormatProfile: boolean;
    showTags: boolean;
    showSearchAction: boolean;
  };
  tableOptions: {
    showBanners: boolean;
    showSearchAction: boolean;
  };
  deleteOptions: {
    addImportListExclusion: boolean;
  };
}

const { useOptions, useOption, setOptions, setOption, setSort, getOptions } =
  createOptionsStore<MangaOptions>('manga_options', () => {
    return {
      selectedFilterKey: 'all',
      sortKey: 'sortTitle',
      sortDirection: 'ascending',
      view: 'posters', // Lock #2 default
      columns: [
        // Plan 07-04 fills the canonical column list (status, sortTitle,
        // originalLanguage, translationProfile, customFormatProfile, chapterCount,
        // chapterProgress, scanlationGroups, translatedLanguages, tags, actions, …).
      ],
      posterOptions: {
        detailedProgressBar: false,
        size: 'medium',
        showTitle: true,
        showMonitored: true,
        showTranslationProfile: true,
        showCustomFormatProfile: false,
        showTags: false,
        showSearchAction: false,
      },
      overviewOptions: {
        detailedProgressBar: false,
        size: 'medium',
        showMonitored: true,
        showTranslationProfile: true,
        showCustomFormatProfile: false,
        showTags: false,
        showSearchAction: false,
      },
      tableOptions: {
        showBanners: false,
        showSearchAction: false,
      },
      deleteOptions: {
        addImportListExclusion: false,
      },
    };
  });

export const useMangaOptions = useOptions;
export const useMangaOption = useOption;
export const setMangaOptions = setOptions;
export const setMangaOption = setOption;
export const setMangaSort = setSort;
export const getMangaOptions = getOptions;
