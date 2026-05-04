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
import translate from 'Utilities/String/translate';

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
    // Sonarr-shape carry-overs so the verbatim-inherited Posters component
    // compiles (Plan 07-04 Lock #10). Manga-only flags above; TV-only flags
    // below stay false-default and remain inert at runtime. Phase 8 cleanup
    // trims these.
    showQualityProfile: boolean;
  };
  overviewOptions: {
    detailedProgressBar: boolean;
    size: 'small' | 'medium' | 'large';
    showMonitored: boolean;
    showTranslationProfile: boolean;
    showCustomFormatProfile: boolean;
    showTags: boolean;
    showSearchAction: boolean;
    // Sonarr-shape carry-overs (see comment on posterOptions above).
    showNetwork: boolean;
    showQualityProfile: boolean;
    showPreviousAiring: boolean;
    showAdded: boolean;
    showSeasonCount: boolean;
    showPath: boolean;
    showSizeOnDisk: boolean;
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
        // Manga column set per Plan 07-04 + RESEARCH.md Example 4.
        // Diverges from seriesOptionsStore by:
        //   * dropping seriesType / network / qualityProfileId / nextAiring /
        //     previousAiring / seasonCount / seasonFolder / episodeProgress /
        //     episodeCount / latestSeason / useSceneNumbering /
        //     monitorNewItems / episodeFileQualities / releaseGroups /
        //     releaseTypes / averageSizePerEpisode
        //   * adding translationProfileId / customFormatProfileId /
        //     chapterCount / chapterProgress / scanlationGroups /
        //     translatedLanguages / metadataSource / contentRating
        //   * keeping status / sortTitle / originalCountry / originalLanguage /
        //     added / year / path / sizeOnDisk / genres / ratings /
        //     certification / tags / actions verbatim.
        {
          name: 'status',
          label: '',
          columnLabel: () => translate('Status'),
          isSortable: true,
          isVisible: true,
          isModifiable: false,
        },
        {
          name: 'sortTitle',
          label: () => translate('MangaTitle'),
          isSortable: true,
          isVisible: true,
          isModifiable: false,
        },
        {
          name: 'originalCountry',
          label: () => translate('OriginalCountry'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'originalLanguage',
          label: () => translate('OriginalLanguage'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'translationProfileId',
          label: () => translate('TranslationProfile'),
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'customFormatProfileId',
          label: () => translate('CustomFormatProfile'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'chapterProgress',
          label: () => translate('Chapters'),
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'chapterCount',
          label: () => translate('ChapterCount'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'scanlationGroups',
          label: () => translate('ScanlationGroups'),
          isSortable: false,
          isVisible: false,
        },
        {
          name: 'translatedLanguages',
          label: () => translate('TranslatedLanguages'),
          isSortable: false,
          isVisible: false,
        },
        {
          name: 'metadataSource',
          label: () => translate('MetadataSource'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'contentRating',
          label: () => translate('ContentRating'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'added',
          label: () => translate('Added'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'year',
          label: () => translate('Year'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'path',
          label: () => translate('Path'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'sizeOnDisk',
          label: () => translate('SizeOnDisk'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'genres',
          label: () => translate('Genres'),
          isSortable: false,
          isVisible: false,
        },
        {
          name: 'ratings',
          label: () => translate('Rating'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'certification',
          label: () => translate('Certification'),
          isSortable: false,
          isVisible: false,
        },
        {
          name: 'tags',
          label: () => translate('Tags'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'actions',
          label: '',
          columnLabel: () => translate('Actions'),
          isVisible: true,
          isModifiable: false,
        },
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
        // Sonarr-shape carry-over (Plan 07-04 Lock #10).
        showQualityProfile: false,
      },
      overviewOptions: {
        detailedProgressBar: false,
        size: 'medium',
        showMonitored: true,
        showTranslationProfile: true,
        showCustomFormatProfile: false,
        showTags: false,
        showSearchAction: false,
        // Sonarr-shape carry-overs (Plan 07-04 Lock #10).
        showNetwork: false,
        showQualityProfile: false,
        showPreviousAiring: false,
        showAdded: false,
        showSeasonCount: false,
        showPath: false,
        showSizeOnDisk: false,
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

// Mirror of Series/seriesOptionsStore.ts:271-301 — partial-options merge helpers
// for each subview so callers can call `setMangaPosterOptions({ size: 'small' })`
// without rebuilding the whole posterOptions block.

export const useMangaPosterOptions = () => useOption('posterOptions');
export const setMangaPosterOptions = (
  options: Partial<MangaOptions['posterOptions']>
) => {
  const currentOptions = getOptions().posterOptions;
  setMangaOption('posterOptions', { ...currentOptions, ...options });
};

export const useMangaOverviewOptions = () => useOption('overviewOptions');
export const setMangaOverviewOptions = (
  options: Partial<MangaOptions['overviewOptions']>
) => {
  const currentOptions = getOptions().overviewOptions;
  setMangaOption('overviewOptions', { ...currentOptions, ...options });
};

export const useMangaTableOptions = () => useOption('tableOptions');
export const setMangaTableOptions = (
  options: Partial<MangaOptions['tableOptions']>
) => {
  const currentOptions = getOptions().tableOptions;
  setMangaOption('tableOptions', { ...currentOptions, ...options });
};

export const useMangaDeleteOptions = () => useOption('deleteOptions');
export const setMangaDeleteOptions = (
  options: Partial<MangaOptions['deleteOptions']>
) => {
  const currentOptions = getOptions().deleteOptions;
  setMangaOption('deleteOptions', { ...currentOptions, ...options });
};
