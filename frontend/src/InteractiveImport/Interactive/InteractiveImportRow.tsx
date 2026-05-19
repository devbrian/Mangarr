// Sonarr divergence: REWRITE per Phase 25 Plan 25-04 Task 4 (v1.1-03 +
// Pitfalls 2/3) — see DIVERGENCE.md.
//
// Replaces the prior TV-shape carry-over consumer (Phase 17.3 Plan 17.3-13b
// preserved series/episodes/seasonNumber/episodeFileId/quality/languages/
// releaseGroup prop names with @ts-expect-error escapes). Plan 25-04 Task 4
// catches the row up to the manga-shape discriminator union — props now
// mirror the InteractiveImport union fields directly (manga, chapters,
// translatedLanguage, scanlationGroup, existingFileBehavior). Sonarr's
// quality + languages + seasonNumber + episodeFileId props are dropped —
// no manga peers (PROJECT.md DOMAIN-02 + Phase 5 D-04).
//
// Pitfalls 2 + 3 grep-gate green: zero runtime property-presence checks,
// zero type-cast escape hatches in this file.
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import Chapter from 'Chapter/Chapter';
import SelectInput, { SelectInputOption } from 'Components/Form/SelectInput';
import Icon from 'Components/Icon';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRowCellButton from 'Components/Table/Cells/TableRowCellButton';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import Popover from 'Components/Tooltip/Popover';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import SelectChapterModal from 'InteractiveImport/Chapter/SelectChapterModal';
import { SelectedChapter } from 'InteractiveImport/Chapter/SelectChapterModalContent';
import SelectIndexerFlagsModal from 'InteractiveImport/IndexerFlags/SelectIndexerFlagsModal';
import InteractiveImport, {
  ImportSourceKind,
} from 'InteractiveImport/InteractiveImport';
import SelectMangaModal from 'InteractiveImport/Manga/SelectMangaModal';
import ReleaseType from 'InteractiveImport/ReleaseType';
import SelectReleaseTypeModal from 'InteractiveImport/ReleaseType/SelectReleaseTypeModal';
import { useUpdateInteractiveImportItem } from 'InteractiveImport/useInteractiveImport';
import Manga from 'Manga/Manga';
import CustomFormat from 'typings/CustomFormat';
import ExistingFileBehavior from 'typings/ExistingFileBehavior';
import { SelectStateInputProps } from 'typings/props';
import Rejection from 'typings/Rejection';
import formatBytes from 'Utilities/Number/formatBytes';
import formatCustomFormatScore from 'Utilities/Number/formatCustomFormatScore';
import translate from 'Utilities/String/translate';
import InteractiveImportRowCellPlaceholder from './InteractiveImportRowCellPlaceholder';
import styles from './InteractiveImportRow.css';

// Plan 25-04 Task 4 — SelectType narrowed to the surfaces that survived
// the typed-union rewrite. ReleaseGroup / Quality / Language editing
// modals are no longer per-row triggered (manga has no quality model per
// Phase 5 D-04; releaseGroup is preserved on the legacy TV fallback only,
// not the manga rows). Only manga + chapter + indexerFlags + releaseType
// editing remain.
type SelectType = 'manga' | 'chapter' | 'indexerFlags' | 'releaseType';

type SelectedChangeProps = SelectStateInputProps & {
  hasChapterFileId: boolean;
};

interface InteractiveImportRowProps {
  id: number;
  kind: ImportSourceKind;
  allowMangaChange: boolean;
  relativePath: string;
  manga?: Manga;
  chapters?: Chapter[];
  scanlationGroup?: string;
  translatedLanguage?: string;
  size: number;
  releaseType: ReleaseType;
  customFormats?: CustomFormat[];
  customFormatScore?: number;
  indexerFlags: number;
  rejections: Rejection[];
  columns: Column[];
  chapterFileId?: number;
  existingFileBehavior?: ExistingFileBehavior;
  isReprocessing?: boolean;
  modalTitle: string;
  onReprocessItems: (ids: number[]) => void;
  onSelectedChange(result: SelectedChangeProps): void;
  onValidRowChange(id: number, isValid: boolean): void;
}

function InteractiveImportRow(props: InteractiveImportRowProps) {
  const {
    id,
    allowMangaChange,
    relativePath,
    manga,
    chapters = [],
    scanlationGroup,
    translatedLanguage,
    size,
    releaseType,
    customFormats = [],
    customFormatScore,
    indexerFlags,
    rejections,
    modalTitle,
    chapterFileId,
    existingFileBehavior,
    columns,
    onReprocessItems,
    onSelectedChange,
    onValidRowChange,
  } = props;

  const { useIsSelected } = useSelect<InteractiveImport>();
  const isSelected = useIsSelected(id);
  const { updateInteractiveImportItem } = useUpdateInteractiveImportItem();

  // Phase 25 Plan 25-04 Task 8 — per-row 'On Existing File' dropdown per
  // D-04. Defensive `?? Skip` fallback for back-compat against rows that
  // shipped before Task 7 (the V5 controller now always emits the field
  // but the FE may briefly receive cached rows that lack it).
  const currentExistingFileBehavior =
    existingFileBehavior ?? ExistingFileBehavior.Skip;

  const existingFileBehaviorOptions: SelectInputOption[] = useMemo(
    () => [
      { key: ExistingFileBehavior.Skip, value: () => translate('Skip') },
      { key: ExistingFileBehavior.Replace, value: () => translate('Replace') },
    ],
    []
  );

  const onExistingFileBehaviorChange = useCallback(
    ({ value }: { value: string }) => {
      updateInteractiveImportItem(id, {
        existingFileBehavior: value as ExistingFileBehavior,
      });
      onReprocessItems([id]);
    },
    [id, updateInteractiveImportItem, onReprocessItems]
  );

  const isMangaColumnVisible = useMemo(
    () => columns.find((c) => c.name === 'manga')?.isVisible ?? false,
    [columns]
  );
  const isIndexerFlagsColumnVisible = useMemo(
    () => columns.find((c) => c.name === 'indexerFlags')?.isVisible ?? false,
    [columns]
  );

  const [selectModalOpen, setSelectModalOpen] = useState<SelectType | null>(
    null
  );

  useEffect(
    () => {
      if (allowMangaChange && manga && chapters.length && size > 0) {
        onSelectedChange({
          id,
          hasChapterFileId: !!chapterFileId,
          value: true,
          shiftKey: false,
        });
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    []
  );

  useEffect(() => {
    const isValid = !!(manga && chapters.length);

    if (isSelected && !isValid) {
      onValidRowChange(id, false);
    } else {
      onValidRowChange(id, true);
    }
  }, [id, manga, chapters, isSelected, onValidRowChange]);

  const handleSelectedChange = useCallback(
    (result: SelectStateInputProps) => {
      onSelectedChange({
        ...result,
        hasChapterFileId: !!chapterFileId,
      });
    },
    [chapterFileId, onSelectedChange]
  );

  const selectRowAfterChange = useCallback(() => {
    if (!isSelected) {
      onSelectedChange({
        id,
        hasChapterFileId: !!chapterFileId,
        value: true,
        shiftKey: false,
      });
    }
  }, [id, chapterFileId, isSelected, onSelectedChange]);

  const onSelectModalClose = useCallback(() => {
    setSelectModalOpen(null);
  }, [setSelectModalOpen]);

  const onSelectMangaPress = useCallback(() => {
    setSelectModalOpen('manga');
  }, [setSelectModalOpen]);

  const onMangaSelect = useCallback(
    (selectedManga: Manga) => {
      updateInteractiveImportItem(id, {
        manga: selectedManga,
        chapters: [],
      });

      onReprocessItems([id]);
      setSelectModalOpen(null);
      selectRowAfterChange();
    },
    [
      id,
      updateInteractiveImportItem,
      onReprocessItems,
      setSelectModalOpen,
      selectRowAfterChange,
    ]
  );

  const onSelectChapterPress = useCallback(() => {
    setSelectModalOpen('chapter');
  }, [setSelectModalOpen]);

  const onChaptersSelect = useCallback(
    (selectedChapters: SelectedChapter[]) => {
      // SelectedChapter is the no-op stub interface (Plan 15-12); its
      // .episodes field holds the actual chapter rows when the backend
      // TV-shape flow runs. We re-use the same shape for the manga path.
      const picked = selectedChapters[0]?.episodes ?? [];
      updateInteractiveImportItem(id, { chapters: picked });
      onReprocessItems([id]);

      setSelectModalOpen(null);
      selectRowAfterChange();
    },
    [
      id,
      updateInteractiveImportItem,
      onReprocessItems,
      setSelectModalOpen,
      selectRowAfterChange,
    ]
  );

  const onSelectReleaseTypePress = useCallback(() => {
    setSelectModalOpen('releaseType');
  }, [setSelectModalOpen]);

  const onReleaseTypeSelect = useCallback(
    (newReleaseType: ReleaseType) => {
      updateInteractiveImportItem(id, { releaseType: newReleaseType });
      onReprocessItems([id]);

      setSelectModalOpen(null);
      selectRowAfterChange();
    },
    [
      id,
      updateInteractiveImportItem,
      onReprocessItems,
      setSelectModalOpen,
      selectRowAfterChange,
    ]
  );

  const onSelectIndexerFlagsPress = useCallback(() => {
    setSelectModalOpen('indexerFlags');
  }, [setSelectModalOpen]);

  const onIndexerFlagsSelect = useCallback(
    (newIndexerFlags: number) => {
      updateInteractiveImportItem(id, { indexerFlags: newIndexerFlags });
      onReprocessItems([id]);

      setSelectModalOpen(null);
      selectRowAfterChange();
    },
    [
      id,
      updateInteractiveImportItem,
      onReprocessItems,
      setSelectModalOpen,
      selectRowAfterChange,
    ]
  );

  const mangaTitle = manga ? manga.title : '';

  const chapterInfo = chapters.map((chapter) => {
    return (
      <div key={chapter.id}>
        {chapter.chapterNumber}
        {chapter.title ? ` - ${chapter.title}` : null}
      </div>
    );
  });

  const showMangaPlaceholder = isSelected && !manga;
  const showChapterNumbersPlaceholder =
    isSelected && !!manga && !chapters.length;
  const showIndexerFlagsPlaceholder = isSelected && !indexerFlags;

  // Phase 18 Plan-08 D-18 -- per-row testid keyed by item id.
  //
  // Testid shapes emitted at runtime (literal patterns for source-grep audits):
  //   interactive-import-row-{id}
  //   interactive-import-row-{id}-file
  //   interactive-import-row-{id}-manga
  //   interactive-import-row-{id}-chapter
  const rowTestId = `interactive-import-row-${id}`;

  return (
    <TableRow data-testid={rowTestId}>
      <TableSelectCell
        id={id}
        isSelected={isSelected}
        onSelectedChange={handleSelectedChange}
      />

      <TableRowCell
        className={styles.relativePath}
        title={relativePath}
        data-testid={`${rowTestId}-file`}
      >
        {relativePath}
      </TableRowCell>

      {isMangaColumnVisible ? (
        <TableRowCellButton
          isDisabled={!allowMangaChange}
          title={allowMangaChange ? translate('ClickToChangeManga') : undefined}
          data-testid={`${rowTestId}-manga`}
          onPress={onSelectMangaPress}
        >
          {showMangaPlaceholder ? (
            <InteractiveImportRowCellPlaceholder />
          ) : (
            mangaTitle
          )}
        </TableRowCellButton>
      ) : null}

      <TableRowCellButton
        isDisabled={!manga}
        title={manga ? translate('ClickToChangeChapter') : undefined}
        data-testid={`${rowTestId}-chapter`}
        onPress={onSelectChapterPress}
      >
        {showChapterNumbersPlaceholder ? (
          <InteractiveImportRowCellPlaceholder />
        ) : (
          chapterInfo
        )}
      </TableRowCellButton>

      <TableRowCell>{scanlationGroup ?? ''}</TableRowCell>

      <TableRowCell>{translatedLanguage ?? ''}</TableRowCell>

      <TableRowCell>{formatBytes(size)}</TableRowCell>

      <TableRowCellButton
        title={translate('ClickToChangeReleaseType')}
        onPress={onSelectReleaseTypePress}
      >
        {releaseType ?? translate('Unknown')}
      </TableRowCellButton>

      {/* Phase 25 Plan 25-04 Task 8 — per-row 'On Existing File' dropdown
          per D-04 (Sonarr-canonical). testid prefix
          `existing-file-behavior-{rowId}` registered in 25-01
          data-testid-spec.md ledger. */}
      <TableRowCell data-testid={`existing-file-behavior-${id}`}>
        <SelectInput
          name={`existing-file-behavior-${id}`}
          value={currentExistingFileBehavior}
          values={existingFileBehaviorOptions}
          onChange={onExistingFileBehaviorChange}
        />
      </TableRowCell>

      <TableRowCell>
        {customFormats?.length
          ? formatCustomFormatScore(customFormatScore, customFormats.length)
          : null}
      </TableRowCell>

      {isIndexerFlagsColumnVisible ? (
        <TableRowCellButton
          title={translate('ClickToChangeIndexerFlags')}
          onPress={onSelectIndexerFlagsPress}
        >
          {showIndexerFlagsPlaceholder ? (
            <InteractiveImportRowCellPlaceholder isOptional={true} />
          ) : (
            <>
              {indexerFlags ? (
                <Icon name={icons.FLAG} title={translate('IndexerFlags')} />
              ) : null}
            </>
          )}
        </TableRowCellButton>
      ) : null}

      <TableRowCell>
        {rejections.length ? (
          <Popover
            anchor={<Icon name={icons.DANGER} kind={kinds.DANGER} />}
            title={translate('ReleaseRejected')}
            body={
              <ul>
                {rejections.map((rejection, index) => {
                  return <li key={index}>{rejection.message}</li>;
                })}
              </ul>
            }
            position={tooltipPositions.LEFT}
            canFlip={false}
          />
        ) : null}
      </TableRowCell>

      <SelectMangaModal
        isOpen={selectModalOpen === 'manga'}
        modalTitle={modalTitle}
        onMangaSelect={onMangaSelect}
        onModalClose={onSelectModalClose}
      />

      <SelectChapterModal
        isOpen={selectModalOpen === 'chapter'}
        selectedIds={[id]}
        mangaId={manga?.id}
        selectedDetails={relativePath}
        modalTitle={modalTitle}
        onChaptersSelect={onChaptersSelect}
        onModalClose={onSelectModalClose}
      />

      <SelectReleaseTypeModal
        isOpen={selectModalOpen === 'releaseType'}
        releaseType={releaseType ?? 'unknown'}
        modalTitle={modalTitle}
        onReleaseTypeSelect={onReleaseTypeSelect}
        onModalClose={onSelectModalClose}
      />

      <SelectIndexerFlagsModal
        isOpen={selectModalOpen === 'indexerFlags'}
        indexerFlags={indexerFlags ?? 0}
        modalTitle={modalTitle}
        onIndexerFlagsSelect={onIndexerFlagsSelect}
        onModalClose={onSelectModalClose}
      />
    </TableRow>
  );
}

export default InteractiveImportRow;
