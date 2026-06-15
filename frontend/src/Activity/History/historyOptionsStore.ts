// Sonarr divergence: column registry rebalanced for manga shape per GH issue #73
// (Plan 15-12 follow-up). The TV column keys are preserved with manga labels;
// `translatedLanguage` + `scanlationGroup` are appended; `quality` /
// `customFormats` / `customFormatScore` / `languages` columns are removed
// (manga has no quality model per Phase 5 D-04).
//
// Mirrors the precedent set by the sibling Wanted/Missing + CutoffUnmet +
// Blocklist option stores: keep Sonarr column keys, flip labels, drop quality
// axes, add manga-specific axes.
//
// Store-name strategy: localStorage key bumped to `manga_history_options` to
// avoid hydrating into a registry that no longer carries Quality / Formats
// columns the user previously toggled. Pre-existing `history_options` entries
// become orphaned and get garbage-collected; no data is lost.
import {
  createOptionsStore,
  PageableOptions,
} from 'Helpers/Hooks/useOptionsStore';
import translate from 'Utilities/String/translate';

export type HistoryOptions = PageableOptions;

const { useOptions, useOption, setOptions, setOption, setSort } =
  createOptionsStore<HistoryOptions>('manga_history_options', () => {
    return {
      pageSize: 20,
      selectedFilterKey: 'all',
      sortKey: 'time',
      sortDirection: 'descending',
      columns: [
        {
          name: 'eventType',
          label: '',
          columnLabel: () => translate('EventType'),
          isVisible: true,
          isModifiable: false,
        },
        {
          name: 'series.sortTitle',
          label: () => translate('MangaTitle'),
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'episode',
          label: () => translate('Chapter'),
          isVisible: true,
        },
        {
          name: 'episodes.title',
          label: () => translate('ChapterTitle'),
          isVisible: true,
        },
        {
          name: 'translatedLanguage',
          label: () => translate('Language'),
          isVisible: true,
        },
        {
          name: 'scanlationGroup',
          label: () => translate('ScanlationGroup'),
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
          name: 'downloadClient',
          label: () => translate('DownloadClient'),
          isVisible: false,
        },
        {
          name: 'indexer',
          label: () => translate('Indexer'),
          isVisible: false,
        },
        {
          name: 'sourceTitle',
          label: () => translate('SourceTitle'),
          isVisible: false,
        },
        {
          name: 'details',
          label: '',
          columnLabel: () => translate('Details'),
          isVisible: true,
          isModifiable: false,
        },
      ],
    };
  });

export const useHistoryOptions = useOptions;
export const setHistoryOptions = setOptions;
export const useHistoryOption = useOption;
export const setHistoryOption = setOption;
export const setHistorySort = setSort;
