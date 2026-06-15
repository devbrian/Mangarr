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

// GH #308: Sonarr's reference Remove modal (v4, what the user runs) uses CHECKBOXES, not
// SELECT dropdowns. The removal options are now plain booleans backing those checkboxes
// (`removeFromClient` locked-checked when mandatory; `blocklist` unchecked by default;
// `skipRedownload` only surfaced when `blocklist` is checked) rather than the inherited
// Sonarr-v5 `removalMethod`/`blocklistMethod` enum strings.
interface QueueRemovalOptions {
  removeFromClient: boolean;
  blocklist: boolean;
  skipRedownload: boolean;
}

export interface QueueOptions extends PageableOptions {
  removalOptions: QueueRemovalOptions;
}

// GH #308: store name bumped `manga_queue_options` → `manga_queue_options_v2` because the
// `removalOptions` shape changed (method-enum strings → checkbox booleans). The store `merge`
// spreads persisted state wholesale over defaults (useOptionsStore.ts:108-118), so a stale
// `{removalMethod, blocklistMethod}` blob would otherwise leave the new booleans `undefined`
// and break the checkbox defaults. Bumping orphans the old key (column visibility / page size /
// sort only — no data loss), matching the documented store-name-bump precedent above.
const { useOptions, useOption, setOptions, setOption, setSort } =
  createOptionsStore<QueueOptions>('manga_queue_options_v2', () => {
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
          name: 'source',
          label: () => translate('Source'),
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
        removeFromClient: true,
        blocklist: false,
        skipRedownload: false,
      },
    };
  });

export const useQueueOptions = useOptions;
export const setQueueOptions = setOptions;
export const useQueueOption = useOption;
export const setQueueOption = setOption;
export const setQueueSort = setSort;
