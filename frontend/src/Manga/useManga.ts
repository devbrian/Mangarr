// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/useSeries.ts (verbatim port with
// manga-shape filter/sort predicate divergences).
//
// Manga sibling preserves: useApiQuery cache shape (queryKey ['/manga']),
// FILTERS array, SORT_PREDICATES + FILTER_PREDICATES table, useMangaIndex
// client-side filter-and-sort plumbing, mutation hooks for save/delete/toggle.
//
// Manga sibling diverges from useSeries:
//   * URL path '/manga' (mirrors backend MangaController route).
//   * FILTERS use manga statuses ('ongoing' / 'completed' / 'hiatus') NOT
//     ('continuing' / 'ended').
//   * Drop Sonarr predicates that touch nextAiring / previousAiring /
//     seasons[] / episodeProgress (manga has no air dates and no seasons —
//     Phase 7 D-03 + PROJECT.md Volumes/Seasons Out-of-Scope).
//   * Add chapter-shape predicates (chapterProgress / chapterCount).
//   * SignalR cache invalidation key '/manga' matches Plan 07-02 handler
//     entry per the 'React Query key contract' in STATE.md.
//
// Phase 8 cleanup: collapse with useSeries when Tv/ deletes.
import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import { FilterBuilderTag } from 'Components/Filter/Builder/FilterBuilderRowValue';
import { Filter, FilterBuilderProp } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { filterBuilderTypes, filterBuilderValueTypes } from 'Helpers/Props';
import { FilterType } from 'Helpers/Props/filterTypes';
import getFilterTypePredicate from 'Helpers/Props/getFilterTypePredicate';
import { SortDirection } from 'Helpers/Props/sortDirections';
import sortByProp from 'Utilities/Array/sortByProp';
import clientSideFilterAndSort from 'Utilities/Filter/clientSideFilterAndSort';
import translate from 'Utilities/String/translate';
import Manga from './Manga';
import { useMangaOptions } from './mangaOptionsStore';

const dateFilterPredicate = (
  itemDate: string | undefined,
  filterValue: string | Date,
  type: FilterType
): boolean => {
  if (!itemDate) {
    return false;
  }
  const predicate = getFilterTypePredicate(type);
  return predicate(itemDate, filterValue);
};

export const FILTERS: Filter[] = [
  {
    key: 'all',
    label: () => translate('All'),
    filters: [],
  },
  {
    key: 'monitored',
    label: () => translate('MonitoredOnly'),
    filters: [
      {
        key: 'monitored',
        value: [true],
        type: 'equal',
      },
    ],
  },
  {
    key: 'unmonitored',
    label: () => translate('UnmonitoredOnly'),
    filters: [
      {
        key: 'monitored',
        value: [false],
        type: 'equal',
      },
    ],
  },
  {
    key: 'ongoing',
    label: () => translate('OngoingOnly'),
    filters: [
      {
        key: 'status',
        value: 'ongoing',
        type: 'equal',
      },
    ],
  },
  {
    key: 'completed',
    label: () => translate('CompletedOnly'),
    filters: [
      {
        key: 'status',
        value: 'completed',
        type: 'equal',
      },
    ],
  },
  {
    key: 'hiatus',
    label: () => translate('HiatusOnly'),
    filters: [
      {
        key: 'status',
        value: 'hiatus',
        type: 'equal',
      },
    ],
  },
  {
    key: 'missing',
    label: () => translate('MissingChapters'),
    filters: [
      {
        key: 'missing',
        value: [true],
        type: 'equal',
      },
    ],
  },
];

const SORT_PREDICATES = {
  status: (item: Manga, _direction: SortDirection) => {
    let result = 0;

    if (item.monitored) {
      result += 2;
    }

    if (item.status === 'ongoing') {
      result++;
    }

    return result;
  },

  sizeOnDisk: (item: Manga, _direction: SortDirection) => {
    return item.statistics?.sizeOnDisk ?? 0;
  },

  chapterProgress: (item: Manga, _direction: SortDirection) => {
    const statistics = item.statistics;
    const chapterCount = statistics?.chapterCount ?? 0;
    const chapterFileCount = statistics?.chapterFileCount ?? 0;

    const progress = chapterCount
      ? (chapterFileCount / chapterCount) * 100
      : 100;

    return progress + chapterCount / 1000000;
  },

  chapterCount: (item: Manga, _direction: SortDirection) => {
    return item.statistics?.totalChapterCount ?? 0;
  },

  // Sonarr divergence: Phase 17.3 D-13/D-14 — dropped originalLanguage
  // SORT_PREDICATE (Manga.ts D-13 trim removed originalLanguage). The
  // FILTER_BUILDER originalLanguage entry below is also dropped; the
  // MangaIndexSortMenu / MangaIndexRow forks dropped the column too.

  ratings: (item: Manga, _direction: SortDirection) => {
    const ratings = item.ratings;
    return ratings?.value ?? 0;
  },
} as const;

const FILTER_PREDICATES = {
  chapterProgress: (item: Manga, filterValue: number, type: FilterType) => {
    const statistics = item.statistics;
    const chapterCount = statistics?.chapterCount ?? 0;
    const chapterFileCount = statistics?.chapterFileCount ?? 0;

    const progress = chapterCount
      ? (chapterFileCount / chapterCount) * 100
      : 100;

    const predicate = getFilterTypePredicate(type);
    return predicate(progress, filterValue);
  },

  missing: (item: Manga, _filterValue: boolean, _type: FilterType) => {
    const statistics = item.statistics;
    const chapterCount = statistics?.chapterCount ?? 0;
    const chapterFileCount = statistics?.chapterFileCount ?? 0;
    return chapterCount - chapterFileCount > 0;
  },

  added: (item: Manga, filterValue: string | Date, type: FilterType) => {
    return dateFilterPredicate(item.added, filterValue, type);
  },

  ratings: (item: Manga, filterValue: number, type: FilterType) => {
    const predicate = getFilterTypePredicate(type);
    const value = item.ratings?.value ?? 0;
    return predicate(value * 10, filterValue);
  },

  ratingVotes: (item: Manga, filterValue: number, type: FilterType) => {
    const predicate = getFilterTypePredicate(type);
    const votes = item.ratings?.votes ?? 0;
    return predicate(votes, filterValue);
  },

  // Sonarr divergence: Phase 17.3 D-13/D-14 — dropped originalLanguage
  // FILTER_PREDICATE (Manga.ts D-13 trim removed originalLanguage).

  sizeOnDisk: (item: Manga, filterValue: number, type: FilterType) => {
    const predicate = getFilterTypePredicate(type);
    const sizeOnDisk = item.statistics?.sizeOnDisk ?? 0;
    return predicate(sizeOnDisk, filterValue);
  },

  chapterCount: (item: Manga, filterValue: number, type: FilterType) => {
    const predicate = getFilterTypePredicate(type);
    const chapterCount = item.statistics?.totalChapterCount ?? 0;
    return predicate(chapterCount, filterValue);
  },
} as const;

export const FILTER_BUILDER: FilterBuilderProp<Manga>[] = [
  {
    name: 'monitored',
    label: () => translate('Monitored'),
    type: filterBuilderTypes.EXACT,
    valueType: filterBuilderValueTypes.BOOL,
  },
  {
    name: 'status',
    label: () => translate('Status'),
    type: filterBuilderTypes.EXACT,
  },
  {
    name: 'title',
    label: () => translate('Title'),
    type: filterBuilderTypes.STRING,
  },
  {
    name: 'translationProfileId',
    label: () => translate('TranslationProfile'),
    type: filterBuilderTypes.EXACT,
  },
  {
    name: 'customFormatProfileId',
    label: () => translate('CustomFormatProfile'),
    type: filterBuilderTypes.EXACT,
  },
  {
    name: 'added',
    label: () => translate('Added'),
    type: filterBuilderTypes.DATE,
    valueType: filterBuilderValueTypes.DATE,
  },
  {
    name: 'chapterProgress',
    label: () => translate('ChapterProgress'),
    type: filterBuilderTypes.NUMBER,
  },
  {
    name: 'path',
    label: () => translate('Path'),
    type: filterBuilderTypes.STRING,
  },
  {
    name: 'rootFolderPath',
    label: () => translate('RootFolderPath'),
    type: filterBuilderTypes.EXACT,
  },
  {
    name: 'sizeOnDisk',
    label: () => translate('SizeOnDisk'),
    type: filterBuilderTypes.NUMBER,
    valueType: filterBuilderValueTypes.BYTES,
  },
  {
    name: 'genres',
    label: () => translate('Genres'),
    type: filterBuilderTypes.ARRAY,
    optionsSelector: function (items: Manga[]) {
      const tagList = items.reduce<FilterBuilderTag<string, string>[]>(
        (acc, manga) => {
          (manga.genres ?? []).forEach((genre) => {
            acc.push({
              id: genre,
              name: genre,
            });
          });

          return acc;
        },
        []
      );

      return tagList.sort(sortByProp('name'));
    },
  },
  // Sonarr divergence: Phase 17.3 D-13/D-14 — dropped originalLanguage
  // FILTER_BUILDER entry (Manga.ts D-13 trim removed originalLanguage; the
  // optionsSelector aggregator referenced the dropped field).
  {
    name: 'ratings',
    label: () => translate('Rating'),
    type: filterBuilderTypes.NUMBER,
  },
  {
    name: 'ratingVotes',
    label: () => translate('RatingVotes'),
    type: filterBuilderTypes.NUMBER,
  },
  {
    name: 'certification',
    label: () => translate('Certification'),
    type: filterBuilderTypes.EXACT,
  },
  {
    name: 'tags',
    label: () => translate('Tags'),
    type: filterBuilderTypes.ARRAY,
    valueType: filterBuilderValueTypes.TAG,
  },
  {
    name: 'year',
    label: () => translate('Year'),
    type: filterBuilderTypes.NUMBER,
  },
];

const DEFAULT_MANGA: Manga[] = [];

const useManga = () => {
  const { data, ...result } = useApiQuery<Manga[]>({
    path: '/manga',
    queryOptions: {
      staleTime: 5 * 60 * 1000,
      gcTime: Infinity,
    },
  });

  const mangaMap = useMemo(() => {
    if (!data) {
      return new Map<number, Manga>();
    }

    return new Map<number, Manga>(data.map((manga) => [manga.id, manga]));
  }, [data]);

  return {
    ...result,
    data: data ?? DEFAULT_MANGA,
    mangaMap,
  };
};

export default useManga;

export const useMangaIndex = () => {
  const { selectedFilterKey, sortKey, sortDirection } = useMangaOptions();
  const { data: mangaData = [], ...queryResult } = useManga();
  const customFilters = useCustomFiltersList('manga');

  const data = useMemo(() => {
    return clientSideFilterAndSort<
      Manga,
      typeof FILTER_PREDICATES,
      typeof SORT_PREDICATES
    >(mangaData, {
      selectedFilterKey,
      filters: FILTERS,
      filterPredicates: FILTER_PREDICATES,
      customFilters,
      sortKey: sortKey as keyof Manga,
      sortDirection,
      secondarySortKey: 'sortTitle',
      secondarySortDirection: 'ascending',
      sortPredicates: SORT_PREDICATES,
    });
  }, [customFilters, mangaData, selectedFilterKey, sortKey, sortDirection]);

  return {
    ...queryResult,
    data: data.data,
    totalItems: data.totalItems,
  };
};

export const useHasManga = () => {
  const { data: mangaData = [] } = useManga();

  return useMemo(() => {
    return mangaData.length > 0;
  }, [mangaData]);
};

export const useSingleManga = (mangaId?: number) => {
  const { mangaMap } = useManga();

  return useMemo(() => {
    if (!mangaId) {
      return undefined;
    }

    return mangaMap.get(mangaId);
  }, [mangaMap, mangaId]);
};

export const useMultipleManga = (mangaIds: number[]) => {
  const { mangaMap } = useManga();

  return useMemo(() => {
    if (mangaIds.length === 0) {
      return DEFAULT_MANGA;
    }

    return mangaIds.reduce((acc: Manga[], mangaId) => {
      const manga = mangaMap.get(mangaId);

      if (manga) {
        acc.push(manga);
      }

      return acc;
    }, []);
  }, [mangaMap, mangaIds]);
};

interface SaveMangaPayload extends Partial<Manga> {
  id: number;
}

interface DeleteMangaPayload {
  deleteFiles?: boolean;
  addImportListExclusion?: boolean;
}

interface ToggleMangaMonitoredPayload {
  monitored: boolean;
}

interface BulkDeleteMangaPayload {
  mangaIds: number[];
  deleteFiles?: boolean;
  addImportListExclusion?: boolean;
}

interface SaveMangaEditorPayload {
  mangaIds: number[];
  monitored?: boolean;
  translationProfileId?: number;
  customFormatProfileId?: number;
  rootFolderPath?: string;
  tags?: number[];
}

// fix(manga-edit-button-no-op): consistency — route by id so the URL matches
// the resource being updated (`/manga/{id}`). The MangaController route
// template is `{id:int?}` so PUT /api/v5/manga and PUT /api/v5/manga/{id}
// both map to UpdateManga; this hook now takes mangaId as the first arg so
// the request URL is unambiguous, matching the conventions of
// useToggleMangaMonitored + useDeleteManga.
export const useSaveManga = (mangaId: number, moveFiles?: boolean) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<Manga, SaveMangaPayload>({
    path: `/manga/${mangaId}`,
    queryParams: {
      moveFiles,
    },
    method: 'PUT',
    mutationOptions: {
      onSuccess: (updatedManga) => {
        queryClient.setQueryData<Manga[]>(['/manga'], (oldManga) => {
          if (!oldManga) {
            return oldManga;
          }

          return oldManga.map((manga) => {
            if (manga.id === updatedManga.id) {
              return {
                ...manga,
                ...updatedManga,
              };
            }

            return manga;
          });
        });
      },
    },
  });

  return {
    saveManga: mutate,
    isSaving: isPending,
    saveError: error,
  };
};

export const useDeleteManga = (
  mangaId: number,
  options: DeleteMangaPayload
) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<unknown, void>({
    path: `/manga/${mangaId}`,
    queryParams: {
      ...options,
    },
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.setQueryData<Manga[]>(['/manga'], (oldManga) => {
          if (!oldManga) {
            return oldManga;
          }

          return oldManga.filter((manga) => manga.id !== mangaId);
        });
      },
    },
  });

  return {
    deleteManga: mutate,
    isDeleting: isPending,
    deleteError: error,
  };
};

// Stub mirroring useSeries.useUpdateSeriesMonitor — not yet wired to a manga
// canonical bulk-monitor endpoint (Phase 7 D-09 explicitly drops the
// SeasonPass concept). Returns no-op shape so MangaIndexSelectFooter compiles;
// a future plan will land the real bulk-monitor flow.
export const useUpdateMangaMonitor = () => {
  return {
    updateMangaMonitor: (() => undefined) as (payload: unknown) => void,
    isUpdatingMangaMonitor: false,
    updateMangaMonitorError: null as unknown,
  };
};

export const useToggleMangaMonitored = (mangaId: number) => {
  const queryClient = useQueryClient();
  const manga = useSingleManga(mangaId);

  const { mutate, isPending, error } = useApiMutation<
    Manga,
    ToggleMangaMonitoredPayload
  >({
    // CR-01 fix: route by id so backend MangaController.RestPutById matches
    // PUT /api/v5/manga/{id:int}. The bare /manga PUT 404s for the toggle path.
    path: `/manga/${mangaId}`,
    method: 'PUT',
    mutationOptions: {
      onSuccess: (updatedManga) => {
        queryClient.setQueryData<Manga[]>(['/manga'], (oldManga) => {
          if (!oldManga) {
            return oldManga;
          }

          return oldManga.map((m) =>
            m.id === updatedManga.id ? updatedManga : m
          );
        });
      },
    },
  });
  const toggleMangaMonitored = useCallback(
    (payload: ToggleMangaMonitoredPayload) => {
      return mutate({ ...manga, ...payload });
    },
    [manga, mutate]
  );

  return {
    toggleMangaMonitored,
    isTogglingMangaMonitored: isPending,
    toggleMangaMonitoredError: error,
  };
};

export const useSaveMangaEditor = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    Manga[],
    SaveMangaEditorPayload
  >({
    path: '/manga/editor',
    method: 'PUT',
    mutationOptions: {
      onSuccess: (updatedMangaList) => {
        queryClient.setQueryData<Manga[]>(['/manga'], (oldManga) => {
          if (!oldManga) {
            return oldManga;
          }

          return oldManga.map((manga) => {
            const updatedMangaData = updatedMangaList.find(
              (updated) => updated.id === manga.id
            );

            if (updatedMangaData) {
              const {
                alternateTitles,
                images,
                rootFolderPath,
                statistics,
                ...propsToUpdate
              } = updatedMangaData;

              return { ...manga, ...propsToUpdate };
            }

            return manga;
          });
        });
      },
    },
  });

  return {
    saveMangaEditor: mutate,
    isSavingMangaEditor: isPending,
    saveMangaEditorError: error,
  };
};

export const useBulkDeleteManga = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    void,
    BulkDeleteMangaPayload
  >({
    path: '/manga/editor',
    method: 'DELETE',
    mutationOptions: {
      onSuccess: (_, variables) => {
        const mangaIds = new Set(variables.mangaIds);

        queryClient.setQueryData<Manga[]>(['/manga'], (oldManga) => {
          if (!oldManga) {
            return oldManga;
          }

          return oldManga.filter((manga) => !mangaIds.has(manga.id));
        });
      },
    },
  });

  return {
    bulkDeleteManga: mutate,
    isBulkDeleting: isPending,
    bulkDeleteError: error,
  };
};
