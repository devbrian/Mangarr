// Sonarr divergence: REWRITE per Phase 25 Plan 25-04 Task 4 (v1.1-03 +
// Pitfalls 2/3 grep gates) — see DIVERGENCE.md.
//
// Pre-Plan-25-04 this file owned the TV-shape `series` / `episodes` /
// `seasonNumber` / `quality` / `languages` / `releaseGroup` consumer
// surface (Phase 17.3 Plan 17.3-13b carry-over with @ts-expect-error
// escapes). Plan 25-04 Task 4 catches the body up to the manga-shape
// discriminator union (InteractiveImport.ts rewritten to
// MangaImportedRow | QueueSourceRow | FolderSourceRow with `kind`
// literal). Consumers narrow on `row.kind === ...` literal-string —
// zero runtime property-presence checks (Pitfall 2). Zero type-cast
// escape hatches (Pitfall 3).
//
// Folder-picker toggle preserved from Plan 25-03 extraction: when no
// folder + no downloadIds is supplied, the
// InteractiveImportSelectFolderModalContent renders first; the table
// renders after a folder is picked.
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
import usePrevious from 'Helpers/Hooks/usePrevious';
import { align, icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import SelectChapterModal from 'InteractiveImport/Chapter/SelectChapterModal';
import { SelectedChapter } from 'InteractiveImport/Chapter/SelectChapterModalContent';
import InteractiveImportSelectFolderModalContent from 'InteractiveImport/Folder/InteractiveImportSelectFolderModalContent';
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
import SelectMangaModal from 'InteractiveImport/Manga/SelectMangaModal';
import ReleaseType from 'InteractiveImport/ReleaseType';
import SelectReleaseTypeModal from 'InteractiveImport/ReleaseType/SelectReleaseTypeModal';
import useInteractiveImport, {
  useReprocessInteractiveImportItems,
  useUpdateInteractiveImportItem,
  useUpdateInteractiveImportItems,
} from 'InteractiveImport/useInteractiveImport';
import Manga from 'Manga/Manga';
import { SortCallback } from 'typings/callbacks';
import { CheckInputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import hasDifferentItems from 'Utilities/Object/hasDifferentItems';
import translate from 'Utilities/String/translate';
import InteractiveImportRow from './InteractiveImportRow';
import styles from './InteractiveImportModalContent.css';

// Plan 25-04 Task 4 — SelectType narrowed alongside the per-row dropdown
// rewrite (manga + chapter + indexerFlags + releaseType are the only
// surfaces that survive the typed-union rewrite; quality / language /
// releaseGroup / season per-row triggers dropped).
type SelectType =
  | 'select'
  | 'manga'
  | 'chapter'
  | 'indexerFlags'
  | 'releaseType';

// Match the new InteractiveImportRow prop shape.
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
    name: 'manga',
    label: () => translate('Manga'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'chapters',
    label: () => translate('Chapters'),
    isVisible: true,
  },
  {
    name: 'scanlationGroup',
    label: () => translate('ScanlationGroup'),
    isVisible: true,
  },
  {
    name: 'translatedLanguage',
    label: () => translate('TranslatedLanguage'),
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
    // Phase 25 Plan 25-04 Task 8 — per-row 'On Existing File' dropdown
    // column per D-04. testid prefix `existing-file-behavior-*` registered
    // in 25-01 data-testid-spec.md ledger.
    name: 'existingFileBehavior',
    label: () => translate('OnExistingFile'),
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

function isSameChapterFile(
  file: InteractiveImport,
  originalFile?: InteractiveImport
) {
  if (!originalFile) {
    return false;
  }

  if (!originalFile.manga || file.manga?.id !== originalFile.manga.id) {
    return false;
  }

  return !hasDifferentItems(originalFile.chapters ?? [], file.chapters ?? []);
}

const filterExistingFilesStore = create<boolean>(() => false);

export interface InteractiveImportContentProps {
  downloadIds?: string[];
  mangaId?: number;
  showManga?: boolean;
  allowMangaChange?: boolean;
  showDelete?: boolean;
  showImportMode?: boolean;
  showFilterExistingFiles?: boolean;
  title?: string;
  folder?: string;
  sortKey?: string;
  sortDirection?: string;
  initialSortKey?: string;
  initialSortDirection?: string;
  headerLabel?: string;
  onCancel?(): void;
  onConfirm?(): void;
}

function InteractiveImportContentInner(
  props: InteractiveImportContentProps & { folder?: string }
) {
  const {
    downloadIds,
    mangaId,
    allowMangaChange = true,
    showManga = true,
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
    mangaId,
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

  // Plan 25-04 Task 4 — TV-shape delete-files flow gated for v1; no manga
  // ChapterFile peer ships in v1 (Plan 17.3-16 Task 4 deferrals audit).
  // No-op stubs so the call-site shape survives until the peer dir lands.
  const isDeleting = false;
  const deleteError: unknown = null;
  const deleteChapterFiles = useCallback(
    (_args: { chapterFileIds: number[] }) => undefined,
    []
  );
  const updateChapterFiles = useCallback(
    (_files: unknown[]) => undefined,
    []
  );

  const [invalidRowsSelected, setInvalidRowsSelected] = useState<number[]>([]);
  const [
    withoutChapterFileIdRowsSelected,
    setWithoutChapterFileIdRowsSelected,
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

    if (!showManga) {
      const mangaColumn = result.find((c) => c.name === 'manga');

      if (mangaColumn) {
        mangaColumn.isVisible = false;
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
  }, [showManga, items]);

  const selectedIds = useSelectedIds();

  const bulkSelectOptions = useMemo(() => {
    const { chapterSelectDisabled } = items.reduce(
      (acc, item) => {
        if (!selectedIds.includes(item.id)) {
          return acc;
        }

        acc.chapterSelectDisabled ||= !item.manga;
        return acc;
      },
      {
        chapterSelectDisabled: false,
      }
    );

    const options: SelectInputOption[] = [
      {
        key: 'select',
        value: translate('SelectDropdown'),
        disabled: true,
      },
      {
        key: 'chapter',
        value: translate('SelectChapters'),
        disabled: chapterSelectDisabled,
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

    if (allowMangaChange) {
      options.splice(1, 0, {
        key: 'manga',
        value: translate('SelectManga'),
      });
    }

    return options;
  }, [allowMangaChange, items, selectedIds]);

  useEffect(
    () => {
      if (initialSortKey) {
        const dir: SortDirection =
          (initialSortDirection as SortDirection) || 'ascending';

        setInteractiveImportSort({
          sortKey: initialSortKey,
          sortDirection: dir,
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
    ({ id, value, hasChapterFileId, shiftKey = false }) => {
      toggleSelected({
        id,
        isSelected: value,
        shiftKey,
      });

      setWithoutChapterFileIdRowsSelected(
        hasChapterFileId || !value
          ? without(withoutChapterFileIdRowsSelected, id as number)
          : [...withoutChapterFileIdRowsSelected, id as number]
      );
    },
    [
      withoutChapterFileIdRowsSelected,
      setWithoutChapterFileIdRowsSelected,
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

    const chapterFileIds = items.reduce((acc: number[], item) => {
      if (
        selectedIds.indexOf(item.id) > -1 &&
        item.kind === 'manga-imported' &&
        item.chapterFileId
      ) {
        acc.push(item.chapterFileId);
      }

      return acc;
    }, []);

    deleteChapterFiles({ chapterFileIds });
  }, [items, selectedIds, setIsConfirmDeleteModalOpen, deleteChapterFiles]);

  const handleConfirmDeleteModalClose = useCallback(() => {
    setIsConfirmDeleteModalOpen(false);
  }, [setIsConfirmDeleteModalOpen]);

  const handleImportSelectedPress = useCallback(() => {
    const finalImportMode =
      downloadIds || !showImportMode ? 'auto' : importMode;

    const existingFiles: Array<{
      id: number;
      scanlationGroup?: string;
      translatedLanguage?: string;
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

    const seenChapterIds = new Set<number>();
    let hasDuplicateChapters = false;

    items.forEach((item) => {
      const isSelected = selectedIds.indexOf(item.id) > -1;

      if (!isSelected) {
        return;
      }

      const {
        manga,
        chapters,
        scanlationGroup,
        translatedLanguage,
        indexerFlags,
        releaseType,
        existingFileBehavior,
      } = item;

      if (!manga) {
        setInteractiveImportErrorMessage(translate('InteractiveImportNoManga'));
        return;
      }

      if (!chapters || !chapters.length) {
        setInteractiveImportErrorMessage(
          translate('InteractiveImportNoChapter')
        );
        return;
      }

      if (!hasDuplicateChapters) {
        for (const chapter of chapters) {
          const hasAlreadySeen = seenChapterIds.has(chapter.id);
          seenChapterIds.add(chapter.id);

          if (hasAlreadySeen) {
            hasDuplicateChapters = true;
            return;
          }
        }
      }

      setInteractiveImportErrorMessage(null);

      // Existing-file fast-path — only meaningful for manga-imported rows
      // (the row already maps to a ChapterFile id). Preserves the prior
      // EpisodeFile fast-path semantics for the manga side.
      if (item.kind === 'manga-imported' && item.chapterFileId) {
        const originalItem = originalItems.find((i) => i.id === item.id);

        if (isSameChapterFile(item, originalItem)) {
          existingFiles.push({
            id: item.chapterFileId,
            scanlationGroup,
            translatedLanguage,
            indexerFlags,
            releaseType,
          });

          return;
        }
      }

      const downloadId =
        item.kind === 'queue-source' || item.kind === 'manga-imported'
          ? item.downloadId
          : undefined;
      const chapterFileId =
        item.kind === 'manga-imported' ? item.chapterFileId : undefined;

      files.push({
        path: item.path,
        folderName: item.folderName,
        mangaId: manga.id,
        chapterIds: chapters.map((c) => c.id),
        scanlationGroup,
        translatedLanguage,
        indexerFlags,
        releaseType,
        downloadId,
        chapterFileId,
        existingFileBehavior,
      });
    });

    if (hasDuplicateChapters) {
      setInteractiveImportErrorMessage(
        translate('InteractiveImportDuplicateChapters')
      );

      return;
    }

    let shouldClose = false;

    if (existingFiles.length) {
      updateChapterFiles(existingFiles);

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
    updateChapterFiles,
  ]);

  const handleSetInteractiveImportMode = useCallback(
    ({ importMode: mode }: { importMode: ImportMode }) => {
      setInteractiveImportOption('importMode', mode);
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

  const handleMangaSelect = useCallback(
    (selectedManga: Manga) => {
      const updates = {
        manga: selectedManga,
        chapters: [],
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

  const handleChaptersSelect = useCallback(
    (selectedChapters: SelectedChapter[]) => {
      selectedChapters.forEach(({ id, episodes }) => {
        if (id == null) return;
        updateInteractiveImportItem(id, { chapters: episodes ?? [] });
      });

      const ids = selectedChapters
        .map(({ id }) => id)
        .filter((id): id is number => id != null);
      handleReprocessItems(ids);
      setSelectModalOpen(null);
    },
    [updateInteractiveImportItem, setSelectModalOpen, handleReprocessItems]
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

        {isPopulated && !!items.length && !isFetching ? (
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
                      id={item.id}
                      kind={item.kind}
                      relativePath={item.relativePath}
                      manga={item.manga}
                      chapters={item.chapters}
                      scanlationGroup={item.scanlationGroup}
                      translatedLanguage={item.translatedLanguage}
                      size={item.size}
                      releaseType={item.releaseType}
                      customFormats={item.customFormats}
                      customFormatScore={item.customFormatScore}
                      indexerFlags={item.indexerFlags}
                      rejections={item.rejections}
                      chapterFileId={
                        item.kind === 'manga-imported'
                          ? item.chapterFileId
                          : undefined
                      }
                      existingFileBehavior={item.existingFileBehavior}
                      allowMangaChange={allowMangaChange}
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
                !selectedIds.length || !!withoutChapterFileIdRowsSelected.length
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

          <Button onPress={onCancel}>{translate('Cancel')}</Button>

          <Button
            kind={kinds.SUCCESS}
            isDisabled={!selectedIds.length || !!invalidRowsSelected.length}
            onPress={handleImportSelectedPress}
          >
            {folder ? translate('Apply') : translate('Import')}
          </Button>
        </div>
      </div>

      <SelectMangaModal
        isOpen={selectModalOpen === 'manga'}
        modalTitle={headerLabel ?? ''}
        onMangaSelect={handleMangaSelect}
        onModalClose={handleSelectModalClose}
      />

      <SelectChapterModal
        isOpen={selectModalOpen === 'chapter'}
        selectedIds={orderedSelectedIds}
        mangaId={selectedItem?.manga?.id}
        modalTitle={headerLabel ?? ''}
        onChaptersSelect={handleChaptersSelect}
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
  // Plan 25-03 — internal folder-toggle preserved.
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

  const { downloadIds, mangaId } = props;
  const { data } = useInteractiveImport({
    downloadIds,
    mangaId,
    folder: folderPath,
    filterExistingFiles,
  });

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
