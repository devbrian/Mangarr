// Sonarr divergence: NEW shared content per Phase 25 Plan 25-03 (D-02 + D-03)
// — see DIVERGENCE.md.
//
// This component is the shared content body for the Manual Import flow.
// It is consumed by:
//   * InteractiveImportModalContent.tsx (Wanted/Missing modal surface — D-03)
//   * AddManga/AddImportPage/InteractiveImportPage.tsx (full inline page at
//     /add/import — D-02)
//
// Extraction surgery (Phase 25 Plan 25-03 Task 1):
//   * Body copied verbatim from InteractiveImportModalContent.tsx (commit
//     before extraction was the post-Phase-17.3 fork that drops EpisodeFile
//     stub hooks and renames the column header to translate('Manga')).
//   * Outer <ModalContent>/<ModalHeader>/<ModalBody>/<ModalFooter> wrappers
//     stripped (the modal wrapper re-adds them; the page provides
//     PageContent + PageContentBody instead).
//   * Internal folder-picker toggle preserved: when no folder + no
//     downloadIds is supplied, the InteractiveImportSelectFolderModalContent
//     is rendered first, then the import table after a folder is picked.
//     Previously this toggle lived in InteractiveImportModal.tsx; pushing
//     it into the shared content keeps the page's PageContent shell thin
//     (no folder-state bookkeeping needed in the page).
//   * Props interface renamed InteractiveImportModalContentProps ->
//     InteractiveImportContentProps; modalTitle dropped + the modal-close
//     callback dropped; new onCancel?(): void / onConfirm?(): void
//     callbacks added in their place.
//
// Pitfall 16 cross-plan boundary: this commit does NOT touch
// frontend/src/Wanted/Missing/Missing.tsx (the modal LOCK guard drop is
// reserved for Plan 25-05 LAST CODE commit).
import { cloneDeep, without } from 'lodash';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { create } from 'zustand';
import { SelectProvider, useSelect } from 'App/Select/SelectContext';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import SelectInput, { SelectInputOption } from 'Components/Form/SelectInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Menu from 'Components/Menu/Menu';
import MenuButton from 'Components/Menu/MenuButton';
import MenuContent from 'Components/Menu/MenuContent';
import SelectedMenuItem from 'Components/Menu/SelectedMenuItem';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// EpisodeFile/EpisodeFile + EpisodeFile/useEpisodeFiles no-op stub imports
// DROPPED (Phase 15 Plan 15-12 STUB types/hooks; useEpisodeFiles returned
// empty data, useDeleteEpisodeFiles + useUpdateEpisodeFiles returned no-op
// callbacks). The delete-files + update-existing-files flow inside this
// modal is gated for v1 per Phase 12 Plan 12-11 LOCK guard; replaced with
// inline no-op stubs to preserve call-site shape until the manga ChapterFile
// peer dir lands (tracked for v1.x — see Plan 17.3-16 Task 4 deferrals audit).
import usePrevious from 'Helpers/Hooks/usePrevious';
import { align, icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import InteractiveImportSelectFolderModalContent from 'InteractiveImport/Folder/InteractiveImportSelectFolderModalContent';
import SelectChapterModal from 'InteractiveImport/Chapter/SelectChapterModal';
import { SelectedChapter } from 'InteractiveImport/Chapter/SelectChapterModalContent';
import ImportMode from 'InteractiveImport/ImportMode';
import SelectIndexerFlagsModal from 'InteractiveImport/IndexerFlags/SelectIndexerFlagsModal';
import InteractiveImport, {
  InteractiveImportCommandOptions,
} from 'InteractiveImport/InteractiveImport';
import {
  setInteractiveImportOption,
  setInteractiveImportSort,
  useInteractiveImportOptions,
} from 'InteractiveImport/interactiveImportOptionsStore';
import SelectLanguageModal from 'InteractiveImport/Language/SelectLanguageModal';
import SelectQualityModal from 'InteractiveImport/Quality/SelectQualityModal';
import SelectReleaseGroupModal from 'InteractiveImport/ReleaseGroup/SelectReleaseGroupModal';
import ReleaseType from 'InteractiveImport/ReleaseType';
import SelectReleaseTypeModal from 'InteractiveImport/ReleaseType/SelectReleaseTypeModal';
import SelectSeasonModal from 'InteractiveImport/Season/SelectSeasonModal';
import SelectSeriesModal from 'InteractiveImport/Series/SelectSeriesModal';
import useInteractiveImport, {
  useReprocessInteractiveImportItems,
  useUpdateInteractiveImportItem,
  useUpdateInteractiveImportItems,
} from 'InteractiveImport/useInteractiveImport';
import Language from 'Language/Language';
import Manga from 'Manga/Manga';
import { QualityModel } from 'Quality/Quality';
import { SortCallback } from 'typings/callbacks';
import { CheckInputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import hasDifferentItems from 'Utilities/Object/hasDifferentItems';
import translate from 'Utilities/String/translate';
import InteractiveImportRow from './InteractiveImportRow';
import styles from './InteractiveImportModalContent.css';

type SelectType =
  | 'select'
  | 'series'
  | 'season'
  | 'episode'
  | 'releaseGroup'
  | 'quality'
  | 'language'
  | 'indexerFlags'
  | 'releaseType';

// TODO: This feels janky to do, but not sure of a better way currently
type OnSelectedChangeCallback = React.ComponentProps<
  typeof InteractiveImportRow
>['onSelectedChange'];

const COLUMNS = [
  {
    name: 'relativePath',
    label: () => translate('RelativePath'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'series',
    // Sonarr divergence: Phase 17.3 Plan 17.3-16 (D-04) — user-visible label
    // swapped from translate('Series') to translate('Manga'); the column key
    // 'series' is preserved per Plan 17.3-13 InteractiveImport LOCK (Phase 12
    // Plan 12-11) but the user-facing label was orphaned when Phase 15-07
    // deleted the bare "Series" i18n key. Pre-existing carry-forward fix-forward.
    label: () => translate('Manga'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'season',
    label: () => translate('Season'),
    isVisible: true,
  },
  {
    name: 'episodes',
    label: () => translate('Chapters'),
    isVisible: true,
  },
  {
    name: 'releaseGroup',
    label: () => translate('ReleaseGroup'),
    isVisible: true,
  },
  {
    name: 'quality',
    label: () => translate('Quality'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'languages',
    label: () => translate('Languages'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'size',
    label: () => translate('Size'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'releaseType',
    label: () => translate('ReleaseType'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'customFormats',
    label: React.createElement(Icon, {
      name: icons.INTERACTIVE,
      title: () => translate('CustomFormatScore'),
    }),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'indexerFlags',
    label: React.createElement(Icon, {
      name: icons.FLAG,
      title: () => translate('IndexerFlags'),
    }),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'rejections',
    label: React.createElement(Icon, {
      name: icons.DANGER,
      kind: kinds.DANGER,
      title: () => translate('Rejections'),
    }),
    isSortable: true,
    isVisible: true,
  },
];

const importModeOptions: SelectInputOption[] = [
  {
    key: 'chooseImportMode',
    value: () => translate('ChooseImportMode'),
    disabled: true,
  },
  {
    key: 'move',
    value: () => translate('MoveFiles'),
  },
  {
    key: 'copy',
    value: () => translate('HardlinkCopyFiles'),
  },
];

function isSameEpisodeFile(
  file: InteractiveImport,
  originalFile?: InteractiveImport
) {
  const { series, seasonNumber, episodes } = file;

  if (!originalFile) {
    return false;
  }

  if (!originalFile.series || series?.id !== originalFile.series.id) {
    return false;
  }

  if (seasonNumber !== originalFile.seasonNumber) {
    return false;
  }

  return !hasDifferentItems(originalFile.episodes, episodes);
}

const filterExistingFilesStore = create<boolean>(() => false);

export interface InteractiveImportContentProps {
  downloadIds?: string[];
  seriesId?: number;
  seasonNumber?: number;
  showSeries?: boolean;
  allowSeriesChange?: boolean;
  showDelete?: boolean;
  showImportMode?: boolean;
  showFilterExistingFiles?: boolean;
  title?: string;
  folder?: string;
  sortKey?: string;
  sortDirection?: string;
  initialSortKey?: string;
  initialSortDirection?: string;
  // Phase 25 Plan 25-03: caller-supplied "header label" replaces the
  // modal-only `modalTitle` prop. The modal wrapper passes its modalTitle
  // through as `title`-prefix string (preserves existing modal header
  // semantics — "<title> - <folder>"); the page passes the i18n
  // 'ManualImport' string directly to PageContent and leaves this empty.
  headerLabel?: string;
  // Phase 25 Plan 25-03: caller-supplied cancel handler. Modal wrapper
  // forwards its close callback; page passes history.push('/').
  onCancel?(): void;
  onConfirm?(): void;
}

function InteractiveImportContentInner(
  props: InteractiveImportContentProps & { folder?: string }
) {
  const {
    downloadIds,
    seriesId,
    seasonNumber,
    allowSeriesChange = true,
    showSeries = true,
    showFilterExistingFiles = false,
    showDelete = false,
    showImportMode = true,
    title,
    folder,
    initialSortKey,
    initialSortDirection,
    headerLabel,
    onCancel,
    onConfirm,
  } = props;

  const filterExistingFiles = filterExistingFilesStore((state) => state);
  const [reprocessingItems, setReprocessingItems] = useState<Set<number>>(
    new Set()
  );

  const {
    isFetching,
    isFetched: isPopulated,
    error,
    data,
    originalItems,
  } = useInteractiveImport({
    downloadIds,
    seriesId,
    seasonNumber,
    folder,
    filterExistingFiles,
  });

  const { sortKey, sortDirection, importMode } = useInteractiveImportOptions();

  const { updateInteractiveImportItem } = useUpdateInteractiveImportItem();
  const { updateInteractiveImportItems } = useUpdateInteractiveImportItems();

  const { reprocessInteractiveImportItems, isReprocessing } =
    useReprocessInteractiveImportItems();

  const wasReprocessing = usePrevious(isReprocessing);

  // Phase 17.3 P-007 defensive `?? []` — `data` from useInteractiveImport is
  // already `?? DEFAULT_ITEMS` inside the hook, but we re-assert here so the
  // downstream `.length` / `.find` / `.reduce` calls never crash if the
  // hook's contract ever drifts.
  const items = data ?? [];

  // Sonarr divergence: Phase 17.3 Plan 17.3-13b — useDeleteEpisodeFiles +
  // useUpdateEpisodeFiles no-op stub hooks replaced with inline no-op
  // call-site shape (TV InteractiveImport flow gated for v1; no Chapter
  // peer ships in v1). v1.x cleanup at ChapterFile peer dir authoring.
  // Wrapped in useCallback so the no-op identities are stable across renders
  // — the downstream useCallback deps (handleDeleteSelectedPress /
  // handleSelectModalSelect) would otherwise change every render
  // (react-hooks/exhaustive-deps).
  const isDeleting = false;
  const deleteError: unknown = null;
  const deleteEpisodeFiles = useCallback(
    (_args: { episodeFileIds: number[] }) => undefined,
    []
  );
  const updateEpisodeFiles = useCallback((_files: unknown[]) => undefined, []);

  const [invalidRowsSelected, setInvalidRowsSelected] = useState<number[]>([]);
  const [
    withoutEpisodeFileIdRowsSelected,
    setWithoutEpisodeFileIdRowsSelected,
  ] = useState<number[]>([]);
  const [selectModalOpen, setSelectModalOpen] = useState<SelectType | null>(
    null
  );
  const [isConfirmDeleteModalOpen, setIsConfirmDeleteModalOpen] =
    useState(false);
  const [interactiveImportErrorMessage, setInteractiveImportErrorMessage] =
    useState<string | null>(null);
  const previousIsDeleting = usePrevious(isDeleting);
  const executeCommand = useExecuteCommand();

  const {
    allSelected,
    allUnselected,
    selectAll,
    unselectAll,
    toggleSelected,
    useSelectedIds,
  } = useSelect<InteractiveImport>();

  const columns: Column[] = useMemo(() => {
    const result: Column[] = cloneDeep(COLUMNS);

    if (!showSeries) {
      const seriesColumn = result.find((c) => c.name === 'series');

      if (seriesColumn) {
        seriesColumn.isVisible = false;
      }
    }

    const showIndexerFlags = items.some((item) => item.indexerFlags);

    if (!showIndexerFlags) {
      const indexerFlagsColumn = result.find((c) => c.name === 'indexerFlags');

      if (indexerFlagsColumn) {
        indexerFlagsColumn.isVisible = false;
      }
    }

    return result;
  }, [showSeries, items]);

  const selectedIds = useSelectedIds();

  const bulkSelectOptions = useMemo(() => {
    const { episodeSelectDisabled } = items.reduce(
      (acc, item) => {
        if (!selectedIds.includes(item.id)) {
          return acc;
        }

        const lastSelectedSeason = acc.lastSelectedSeason;

        acc.seasonSelectDisabled ||= !item.series;
        acc.episodeSelectDisabled ||=
          item.seasonNumber === undefined ||
          (lastSelectedSeason >= 0 && item.seasonNumber !== lastSelectedSeason);
        acc.lastSelectedSeason = item.seasonNumber ?? -1;

        return acc;
      },
      {
        seasonSelectDisabled: false,
        episodeSelectDisabled: false,
        lastSelectedSeason: -1,
      }
    );

    const options: SelectInputOption[] = [
      {
        key: 'select',
        value: translate('SelectDropdown'),
        disabled: true,
      },
      // Sonarr divergence: Phase 17.3 Plan 17.3-14 — 'season' option dropped
      // (manga has no seasons per DOMAIN-02). 'episode' option renamed to 'chapter'
      // i18n key SelectChapters.
      {
        key: 'episode',
        value: translate('SelectChapters'),
        disabled: episodeSelectDisabled,
      },
      {
        key: 'quality',
        value: translate('SelectQuality'),
      },
      {
        key: 'releaseGroup',
        value: translate('SelectReleaseGroup'),
      },
      {
        key: 'language',
        value: translate('SelectLanguage'),
      },
      {
        key: 'indexerFlags',
        value: translate('SelectIndexerFlags'),
      },
      {
        key: 'releaseType',
        value: translate('SelectReleaseType'),
      },
    ];

    if (allowSeriesChange) {
      options.splice(1, 0, {
        key: 'series',
        value: translate('SelectManga'),
      });
    }

    return options;
  }, [allowSeriesChange, items, selectedIds]);

  useEffect(
    () => {
      if (initialSortKey) {
        const sortDirection: SortDirection =
          (initialSortDirection as SortDirection) || 'ascending';

        setInteractiveImportSort({
          sortKey: initialSortKey,
          sortDirection,
        });
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    []
  );

  useEffect(() => {
    if (!isDeleting && previousIsDeleting && !deleteError) {
      onCancel?.();
    }
  }, [previousIsDeleting, isDeleting, deleteError, onCancel]);

  const handleSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) => {
      if (value) {
        selectAll();
      } else {
        unselectAll();
      }
    },
    [selectAll, unselectAll]
  );

  const handleSelectedChange = useCallback<OnSelectedChangeCallback>(
    ({ id, value, hasEpisodeFileId, shiftKey = false }) => {
      toggleSelected({
        id,
        isSelected: value,
        shiftKey,
      });

      setWithoutEpisodeFileIdRowsSelected(
        hasEpisodeFileId || !value
          ? without(withoutEpisodeFileIdRowsSelected, id as number)
          : [...withoutEpisodeFileIdRowsSelected, id as number]
      );
    },
    [
      withoutEpisodeFileIdRowsSelected,
      setWithoutEpisodeFileIdRowsSelected,
      toggleSelected,
    ]
  );

  const handleValidRowChange = useCallback(
    (id: number, isValid: boolean) => {
      if (isValid && invalidRowsSelected.includes(id)) {
        setInvalidRowsSelected(without(invalidRowsSelected, id));
      } else if (!isValid && !invalidRowsSelected.includes(id)) {
        setInvalidRowsSelected([...invalidRowsSelected, id]);
      }
    },
    [invalidRowsSelected, setInvalidRowsSelected]
  );

  const handleDeleteSelectedPress = useCallback(() => {
    setIsConfirmDeleteModalOpen(true);
  }, [setIsConfirmDeleteModalOpen]);

  const handleConfirmDelete = useCallback(() => {
    setIsConfirmDeleteModalOpen(false);

    const episodeFileIds = items.reduce((acc: number[], item) => {
      if (selectedIds.indexOf(item.id) > -1 && item.episodeFileId) {
        acc.push(item.episodeFileId);
      }

      return acc;
    }, []);

    deleteEpisodeFiles({ episodeFileIds });
  }, [items, selectedIds, setIsConfirmDeleteModalOpen, deleteEpisodeFiles]);

  const handleConfirmDeleteModalClose = useCallback(() => {
    setIsConfirmDeleteModalOpen(false);
  }, [setIsConfirmDeleteModalOpen]);

  const handleImportSelectedPress = useCallback(() => {
    const finalImportMode =
      downloadIds || !showImportMode ? 'auto' : importMode;

    // Sonarr divergence: Phase 17.3 Plan 17.3-13b — EpisodeFile stub type
    // dropped; use a structural shape matching the prior stub
    // (seriesId/episodeNumbers/etc. preserved at runtime because the TV
    // InteractiveImport backend is gated for v1).
    const existingFiles: Array<{
      id: number;
      releaseGroup?: string;
      quality?: unknown;
      languages?: unknown;
      indexerFlags?: number;
      releaseType?: unknown;
    }> = [];
    const files: InteractiveImportCommandOptions[] = [];

    if (finalImportMode === 'chooseImportMode') {
      setInteractiveImportErrorMessage(
        translate('InteractiveImportNoImportMode')
      );

      return;
    }

    const seenEpisodeIds = new Set<number>();
    let hasDuplicateEpisodes = false;

    items.forEach((item) => {
      const isSelected = selectedIds.indexOf(item.id) > -1;

      if (isSelected) {
        const {
          downloadId,
          series,
          seasonNumber,
          episodes,
          releaseGroup,
          quality,
          languages,
          indexerFlags,
          episodeFileId,
          releaseType,
        } = item;

        if (!series) {
          setInteractiveImportErrorMessage(
            translate('InteractiveImportNoManga')
          );
          return;
        }

        if (isNaN(seasonNumber)) {
          setInteractiveImportErrorMessage(
            translate('InteractiveImportNoChapter')
          );
          return;
        }

        if (!episodes || !episodes.length) {
          setInteractiveImportErrorMessage(
            translate('InteractiveImportNoChapter')
          );
          return;
        }

        if (!quality) {
          setInteractiveImportErrorMessage(
            translate('InteractiveImportNoQuality')
          );
          return;
        }

        if (!languages) {
          setInteractiveImportErrorMessage(
            translate('InteractiveImportNoLanguage')
          );
          return;
        }

        if (!hasDuplicateEpisodes) {
          for (const episode of episodes) {
            const hasAlreadySeen = seenEpisodeIds.has(episode.id);

            seenEpisodeIds.add(episode.id);

            if (hasAlreadySeen) {
              hasDuplicateEpisodes = true;
              return;
            }
          }
        }

        setInteractiveImportErrorMessage(null);

        if (episodeFileId) {
          const originalItem = originalItems.find((i) => i.id === item.id);

          if (isSameEpisodeFile(item, originalItem)) {
            // Sonarr divergence: Phase 17.3 Plan 17.3-13b — EpisodeFile stub
            // type dropped (Plan 15-12 cast-to-any pattern superseded); the
            // inline structural shape on existingFiles now covers
            // indexerFlags/releaseType. Manga ChapterFile peer deferred to
            // v1.x (Plan 17.3-16 Task 4 deferrals audit).
            existingFiles.push({
              id: episodeFileId,
              releaseGroup,
              quality,
              languages,
              indexerFlags,
              releaseType,
            });

            return;
          }
        }

        files.push({
          path: item.path,
          folderName: item.folderName,
          seriesId: series.id,
          episodeIds: episodes.map((e) => e.id),
          releaseGroup,
          quality,
          languages,
          indexerFlags,
          releaseType,
          downloadId,
          episodeFileId,
        });
      }
    });

    if (hasDuplicateEpisodes) {
      setInteractiveImportErrorMessage(
        translate('InteractiveImportDuplicateChapters')
      );

      return;
    }

    let shouldClose = false;

    if (existingFiles.length) {
      updateEpisodeFiles(existingFiles);

      shouldClose = true;
    }

    if (files.length) {
      executeCommand({
        name: CommandNames.ManualImport,
        files,
        importMode: finalImportMode,
        priority: 'high',
      });

      shouldClose = true;
    }

    if (shouldClose) {
      onConfirm?.();
      onCancel?.();
    }
  }, [
    downloadIds,
    showImportMode,
    importMode,
    items,
    originalItems,
    selectedIds,
    onCancel,
    onConfirm,
    executeCommand,
    updateEpisodeFiles,
  ]);

  const handleSetInteractiveImportMode = useCallback(
    ({ importMode }: { importMode: ImportMode }) => {
      setInteractiveImportOption('importMode', importMode);
    },
    []
  );

  const handleSortPress = useCallback<SortCallback>(
    (sortKey, sortDirection) => {
      setInteractiveImportSort({ sortKey, sortDirection });
    },
    []
  );

  const handleFilterExistingFilesChange = useCallback(
    (value: string | undefined) => {
      const filter = value !== 'all';
      filterExistingFilesStore.setState(filter);
    },
    []
  );

  const handleImportModeChange = useCallback<
    ({ value }: { value: ImportMode }) => void
  >(
    ({ value }) => {
      handleSetInteractiveImportMode({ importMode: value });
    },
    [handleSetInteractiveImportMode]
  );

  const handleSelectModalSelect = useCallback<
    ({ value }: { value: SelectType }) => void
  >(
    ({ value }) => {
      setSelectModalOpen(value);
    },
    [setSelectModalOpen]
  );

  const handleSelectModalClose = useCallback(() => {
    setSelectModalOpen(null);
  }, [setSelectModalOpen]);

  const handleReprocessItems = useCallback(
    (ids: number[]) => {
      setReprocessingItems((prev) => {
        const newSet = new Set(prev);

        ids.forEach((id) => newSet.add(id));

        return newSet;
      });

      reprocessInteractiveImportItems(ids);
    },
    [reprocessInteractiveImportItems]
  );

  const handleSeriesSelect = useCallback(
    (series: Manga) => {
      const updates = {
        series,
        seasonNumber: undefined,
        episodes: [],
      };

      updateInteractiveImportItems(selectedIds, updates);

      handleReprocessItems(selectedIds);
      setSelectModalOpen(null);
    },
    [
      selectedIds,
      updateInteractiveImportItems,
      setSelectModalOpen,
      handleReprocessItems,
    ]
  );

  const handleSeasonSelect = useCallback(
    (seasonNumber: number) => {
      const updates = {
        seasonNumber,
        episodes: [],
      };

      updateInteractiveImportItems(selectedIds, updates);
      handleReprocessItems(selectedIds);

      setSelectModalOpen(null);
    },
    [
      selectedIds,
      setSelectModalOpen,
      updateInteractiveImportItems,
      handleReprocessItems,
    ]
  );

  const handleEpisodesSelect = useCallback(
    (selectedEpisodes: SelectedChapter[]) => {
      selectedEpisodes.forEach(({ id, episodes }) => {
        if (id == null) return;
        updateInteractiveImportItem(id, { episodes });
      });

      const selectedIds = selectedEpisodes
        .map(({ id }) => id)
        .filter((id): id is number => id != null);
      handleReprocessItems(selectedIds);
      setSelectModalOpen(null);
    },
    [updateInteractiveImportItem, setSelectModalOpen, handleReprocessItems]
  );

  const handleReleaseGroupSelect = useCallback(
    (releaseGroup: string) => {
      updateInteractiveImportItems(selectedIds, { releaseGroup });

      handleReprocessItems(selectedIds);
      setSelectModalOpen(null);
    },
    [
      selectedIds,
      updateInteractiveImportItems,
      setSelectModalOpen,
      handleReprocessItems,
    ]
  );

  const handleLanguagesSelect = useCallback(
    (newLanguages: Language[]) => {
      updateInteractiveImportItems(selectedIds, { languages: newLanguages });

      handleReprocessItems(selectedIds);
      setSelectModalOpen(null);
    },
    [
      selectedIds,
      updateInteractiveImportItems,
      setSelectModalOpen,
      handleReprocessItems,
    ]
  );

  const handleQualitySelect = useCallback(
    (quality: QualityModel) => {
      updateInteractiveImportItems(selectedIds, { quality });

      handleReprocessItems(selectedIds);
      setSelectModalOpen(null);
    },
    [
      selectedIds,
      updateInteractiveImportItems,
      setSelectModalOpen,
      handleReprocessItems,
    ]
  );

  const handleIndexerFlagsSelect = useCallback(
    (indexerFlags: number) => {
      updateInteractiveImportItems(selectedIds, { indexerFlags });

      handleReprocessItems(selectedIds);
      setSelectModalOpen(null);
    },
    [
      selectedIds,
      updateInteractiveImportItems,
      setSelectModalOpen,
      handleReprocessItems,
    ]
  );

  const handleReleaseTypeSelect = useCallback(
    (releaseType: string) => {
      updateInteractiveImportItems(selectedIds, {
        releaseType: releaseType as ReleaseType,
      });

      handleReprocessItems(selectedIds);
      setSelectModalOpen(null);
    },
    [
      selectedIds,
      updateInteractiveImportItems,
      setSelectModalOpen,
      handleReprocessItems,
    ]
  );

  const orderedSelectedIds = items.reduce((acc: number[], file) => {
    if (selectedIds.includes(file.id)) {
      acc.push(file.id);
    }

    return acc;
  }, []);

  const selectedItem = selectedIds.length
    ? items.find((file) => file.id === selectedIds[0])
    : null;

  const errorMessage = getErrorMessage(
    error,
    translate('InteractiveImportLoadError')
  );

  useEffect(() => {
    if (!isReprocessing && wasReprocessing) {
      setReprocessingItems(new Set());
    }
  }, [isReprocessing, wasReprocessing]);

  return (
    // Phase 25 Plan 25-03 — shared content body (no Modal chrome). Consumed by
    // InteractiveImportModalContent (modal wrapper) and InteractiveImportPage
    // (full inline page). The `interactive-import-content` testid follows the
    // `manual-import-*` allowed prefix family per Phase 18 D-18 / Plan 25-01.
    <div data-testid="interactive-import-content">
      <div>
        {headerLabel ? (
          <h2>
            {headerLabel}
            {title || folder ? ` - ${title || folder}` : null}
          </h2>
        ) : null}

        {showFilterExistingFiles ? (
          <div className={styles.filterContainer}>
            <Menu alignMenu={align.RIGHT}>
              <MenuButton>
                <Icon name={icons.FILTER} size={22} />

                <div className={styles.filterText}>
                  {filterExistingFiles
                    ? translate('UnmappedFilesOnly')
                    : translate('AllFiles')}
                </div>
              </MenuButton>

              <MenuContent>
                <SelectedMenuItem
                  name="all"
                  isSelected={!filterExistingFiles}
                  onPress={handleFilterExistingFilesChange}
                >
                  {translate('AllFiles')}
                </SelectedMenuItem>

                <SelectedMenuItem
                  name="new"
                  isSelected={filterExistingFiles}
                  onPress={handleFilterExistingFilesChange}
                >
                  {translate('UnmappedFilesOnly')}
                </SelectedMenuItem>
              </MenuContent>
            </Menu>
          </div>
        ) : null}

        {isFetching ? <LoadingIndicator /> : null}

        {error ? <div>{errorMessage}</div> : null}

        {isPopulated && !!items.length && !isFetching && !isFetching ? (
          // Phase 18 Plan-08 -- interactive-import-modal-table testid wrapper.
          <div data-testid="interactive-import-modal-table">
            <Table
              columns={columns}
              horizontalScroll={true}
              selectAll={true}
              allSelected={allSelected}
              allUnselected={allUnselected}
              sortKey={sortKey}
              sortDirection={sortDirection}
              onSortPress={handleSortPress}
              onSelectAllChange={handleSelectAllChange}
            >
              <TableBody>
                {items.map((item) => {
                  return (
                    <InteractiveImportRow
                      key={item.id}
                      {...item}
                      allowSeriesChange={allowSeriesChange}
                      columns={columns}
                      modalTitle={headerLabel ?? ''}
                      isReprocessing={reprocessingItems.has(item.id)}
                      onReprocessItems={handleReprocessItems}
                      onSelectedChange={handleSelectedChange}
                      onValidRowChange={handleValidRowChange}
                    />
                  );
                })}
              </TableBody>
            </Table>
          </div>
        ) : null}

        {isPopulated && !items.length && !isFetching
          ? translate('InteractiveImportNoFilesFound')
          : null}
      </div>

      <div className={styles.footer}>
        <div className={styles.leftButtons}>
          {showDelete ? (
            <SpinnerButton
              className={styles.deleteButton}
              kind={kinds.DANGER}
              isSpinning={isDeleting}
              isDisabled={
                !selectedIds.length || !!withoutEpisodeFileIdRowsSelected.length
              }
              onPress={handleDeleteSelectedPress}
            >
              {translate('Delete')}
            </SpinnerButton>
          ) : null}

          {!downloadIds && showImportMode ? (
            <SelectInput
              className={styles.importMode}
              name="importMode"
              value={importMode}
              values={importModeOptions}
              onChange={handleImportModeChange}
            />
          ) : null}

          <SelectInput
            className={styles.bulkSelect}
            name="select"
            value="select"
            values={bulkSelectOptions}
            isDisabled={!selectedIds.length}
            onChange={handleSelectModalSelect}
          />
        </div>

        <div className={styles.rightButtons}>
          {interactiveImportErrorMessage ? (
            <span className={styles.errorMessage}>
              {interactiveImportErrorMessage}
            </span>
          ) : null}

          <Button onPress={onCancel}>Cancel</Button>

          <Button
            kind={kinds.SUCCESS}
            isDisabled={!selectedIds.length || !!invalidRowsSelected.length}
            onPress={handleImportSelectedPress}
          >
            {folder ? translate('Apply') : translate('Import')}
          </Button>
        </div>
      </div>

      <SelectSeriesModal
        isOpen={selectModalOpen === 'series'}
        modalTitle={headerLabel ?? ''}
        onSeriesSelect={handleSeriesSelect}
        onModalClose={handleSelectModalClose}
      />

      <SelectSeasonModal
        isOpen={selectModalOpen === 'season'}
        seriesId={selectedItem?.series?.id}
        modalTitle={headerLabel ?? ''}
        onSeasonSelect={handleSeasonSelect}
        onModalClose={handleSelectModalClose}
      />

      {/* Sonarr divergence: Phase 17.3 D-13/D-14 — dropped
          isAnime={selectedItem?.series?.seriesType === 'anime'} prop pass
          (manga has no anime-format; seriesType removed from Manga.ts per
          D-13). */}
      <SelectChapterModal
        isOpen={selectModalOpen === 'episode'}
        selectedIds={orderedSelectedIds}
        seriesId={selectedItem?.series?.id}
        seasonNumber={selectedItem?.seasonNumber}
        modalTitle={headerLabel ?? ''}
        onEpisodesSelect={handleEpisodesSelect}
        onModalClose={handleSelectModalClose}
      />

      <SelectReleaseGroupModal
        isOpen={selectModalOpen === 'releaseGroup'}
        releaseGroup=""
        modalTitle={headerLabel ?? ''}
        onReleaseGroupSelect={handleReleaseGroupSelect}
        onModalClose={handleSelectModalClose}
      />

      <SelectLanguageModal
        isOpen={selectModalOpen === 'language'}
        languageIds={[0]}
        modalTitle={headerLabel ?? ''}
        onLanguagesSelect={handleLanguagesSelect}
        onModalClose={handleSelectModalClose}
      />

      <SelectQualityModal
        isOpen={selectModalOpen === 'quality'}
        qualityId={0}
        proper={false}
        real={false}
        modalTitle={headerLabel ?? ''}
        onQualitySelect={handleQualitySelect}
        onModalClose={handleSelectModalClose}
      />

      <SelectIndexerFlagsModal
        isOpen={selectModalOpen === 'indexerFlags'}
        indexerFlags={0}
        modalTitle={headerLabel ?? ''}
        onIndexerFlagsSelect={handleIndexerFlagsSelect}
        onModalClose={handleSelectModalClose}
      />

      <SelectReleaseTypeModal
        isOpen={selectModalOpen === 'releaseType'}
        releaseType="unknown"
        modalTitle={headerLabel ?? ''}
        onReleaseTypeSelect={handleReleaseTypeSelect}
        onModalClose={handleSelectModalClose}
      />

      <ConfirmModal
        isOpen={isConfirmDeleteModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteSelectedChapterFiles')}
        message={translate('DeleteSelectedChapterFilesHelpText')}
        confirmLabel={translate('Delete')}
        onConfirm={handleConfirmDelete}
        onCancel={handleConfirmDeleteModalClose}
      />
    </div>
  );
}

function InteractiveImportContent(props: InteractiveImportContentProps) {
  // Phase 25 Plan 25-03 — internal folder-toggle preserved from the prior
  // InteractiveImportModal.tsx behavior. When the caller did not pre-supply
  // a folder AND there are no downloadIds, render the folder picker first;
  // the picker's onFolderSelect transitions to the import-table view. This
  // keeps the page shell (PageContent + PageContentBody) thin — the page
  // does not need to own any folder-state bookkeeping.
  const [folderPath, setFolderPath] = useState<string | undefined>(
    props.folder
  );

  const handleFolderSelect = useCallback((path: string) => {
    setFolderPath(path);
  }, []);

  useEffect(() => {
    setFolderPath(props.folder);
  }, [props.folder]);

  const filterExistingFiles = filterExistingFilesStore((state) => state);

  const { downloadIds, seriesId, seasonNumber } = props;
  const { data } = useInteractiveImport({
    downloadIds,
    seriesId,
    seasonNumber,
    folder: folderPath,
    filterExistingFiles,
  });

  // Folder picker first run — show the picker until a folder is chosen (or
  // downloadIds is supplied, in which case the table renders immediately).
  if (!folderPath && !downloadIds) {
    return (
      <div data-testid="interactive-import-content">
        <InteractiveImportSelectFolderModalContent
          modalTitle={props.headerLabel ?? translate('ManualImport')}
          onFolderSelect={handleFolderSelect}
          onModalClose={props.onCancel ?? (() => undefined)}
        />
      </div>
    );
  }

  return (
    <SelectProvider<InteractiveImport> items={data ?? []}>
      <InteractiveImportContentInner {...props} folder={folderPath} />
    </SelectProvider>
  );
}

export default InteractiveImportContent;
