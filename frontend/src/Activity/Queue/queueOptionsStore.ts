// Sonarr divergence: column registry rebalanced for manga shape per GH issue #73
// (Plan 15-12 follow-up). The Sonarr column keys (`series.sortTitle`, `episode`,
// `episodes.title`) are preserved so persisted Zustand state keeps working; the
// labels are relabeled to manga terminology, and the manga-only columns
// (`translatedLanguage`, `scanlationGroup`) are appended. The TV-only columns
// (`quality`, `customFormats`, `customFormatScore`, `languages`,
// `episodes.airDateUtc`) are removed from the registry — manga has no quality
// model per Phase 5 D-04 and no episodic air-date semantics per Phase 7 D-03.
//
// Mirrors the precedent set by `frontend/src/Wanted/Missing/missingOptionsStore.ts`
// + `frontend/src/Wanted/CutoffUnmet/cutoffUnmetOptionsStore.ts` +
// `frontend/src/Activity/Blocklist/blocklistOptionsStore.ts`: keep Sonarr column
// keys, flip labels, drop quality axes, add the manga-specific axes
// (translatedLanguage + scanlationGroup).
//
// Store-name strategy: the localStorage key is bumped to `manga_queue_options` to
// avoid hydrating users into a registry that no longer has the Quality / Formats
// columns they previously toggled visible. Pre-existing `queue_options` localStorage
// entries become orphaned and get garbage-collected on next browser cleanup; no
// data is lost (this store carries only column visibility + page size + sort key).
import {
  createOptionsStore,
  PageableOptions,
} from 'Helpers/Hooks/useOptionsStore';
import translate from 'Utilities/String/translate';

interface QueueRemovalOptions {
  removalMethod: 'changeCategory' | 'ignore' | 'removeFromClient';
  blocklistMethod: 'blocklistAndSearch' | 'blocklistOnly' | 'doNotBlocklist';
}

export interface QueueOptions extends PageableOptions {
  removalOptions: QueueRemovalOptions;
}

const { useOptions, useOption, setOptions, setOption, setSort } =
  createOptionsStore<QueueOptions>('manga_queue_options', () => {
    return {
      pageSize: 20,
      selectedFilterKey: 'all',
      sortKey: 'time',
      sortDirection: 'descending',
      columns: [
        {
          name: 'status',
          label: '',
          columnLabel: () => translate('Status'),
          isSortable: true,
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
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'episodes.title',
          label: () => translate('ChapterTitle'),
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'translatedLanguage',
          label: () => translate('Language'),
          isSortable: false,
          isVisible: true,
        },
        {
          name: 'scanlationGroup',
          label: () => translate('ScanlationGroup'),
          isSortable: false,
          isVisible: true,
        },
        {
          name: 'protocol',
          label: () => translate('Protocol'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'indexer',
          label: () => translate('Indexer'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'downloadClient',
          label: () => translate('DownloadClient'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'title',
          label: () => translate('ReleaseTitle'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'size',
          label: () => translate('Size'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'outputPath',
          label: () => translate('OutputPath'),
          isSortable: false,
          isVisible: false,
        },
        {
          name: 'estimatedCompletionTime',
          label: () => translate('TimeLeft'),
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'added',
          label: () => translate('Added'),
          isSortable: true,
          isVisible: false,
        },
        {
          name: 'progress',
          label: () => translate('Progress'),
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'actions',
          label: '',
          columnLabel: () => translate('Actions'),
          isVisible: true,
          isModifiable: false,
        },
      ],
      removalOptions: {
        removalMethod: 'removeFromClient',
        blocklistMethod: 'doNotBlocklist',
      },
    };
  });

export const useQueueOptions = useOptions;
export const setQueueOptions = setOptions;
export const useQueueOption = useOption;
export const setQueueOption = setOption;
export const setQueueSort = setSort;
