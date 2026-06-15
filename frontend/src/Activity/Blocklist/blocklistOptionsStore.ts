// Sonarr divergence: column registry rebalanced for manga shape per GH issue #73
// (Plan 15-12 follow-up). Drops TV-only `quality` / `customFormats` /
// `languages` columns (manga has no quality model per Phase 5 D-04); adds
// `translatedLanguage` (manga's BCP-47 single-string) + `reason` (manga's
// analog of TV `message`). Column keys preserved verbatim from the pre-fix
// store for Zustand persistence continuity; labels are flipped to manga
// terminology.
//
// Store-name strategy: localStorage key bumped to `manga_blocklist_options`
// so users on the upgrade path hydrate the new registry cleanly; pre-existing
// `blocklist_options` entries become orphaned (no data loss — column
// visibility + page size + sort key only).
import {
  createOptionsStore,
  PageableOptions,
} from 'Helpers/Hooks/useOptionsStore';
import translate from 'Utilities/String/translate';

export type BlocklistOptions = PageableOptions;

const { useOptions, useOption, setOptions, setOption, setSort } =
  createOptionsStore<BlocklistOptions>('manga_blocklist_options', () => {
    return {
      pageSize: 20,
      selectedFilterKey: 'all',
      sortKey: 'time',
      sortDirection: 'descending',
      columns: [
        {
          name: 'series.sortTitle',
          label: () => translate('MangaTitle'),
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'sourceTitle',
          label: () => translate('SourceTitle'),
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'translatedLanguage',
          label: () => translate('Language'),
          isVisible: true,
        },
        {
          name: 'source',
          label: () => translate('Source'),
          isSortable: false,
          isVisible: true,
        },
        {
          name: 'date',
          label: () => translate('Date'),
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'reason',
          label: () => translate('Reason'),
          isVisible: true,
        },
        {
          name: 'indexer',
          label: () => translate('Indexer'),
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
    };
  });

export const useBlocklistOptions = useOptions;
export const setBlocklistOptions = setOptions;
export const useBlocklistOption = useOption;
export const setBlocklistOption = setOption;
export const setBlocklistSort = setSort;
