import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useSelect } from 'App/Select/SelectContext';
// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// Episode/Episode rewritten to Chapter/Chapter peer (the TV-shape field names
// episodeNumber + title + id on the row's `episodes` prop are runtime-emitted
// by the backend TV InteractiveImport flow which is gated for v1; type-level
// access is preserved via @ts-expect-error per the Plan 17.3-13 Wanted/* +
// InteractiveImport core precedent). Episode/EpisodeFormats, EpisodeLanguages,
// EpisodeQuality, getReleaseTypeName, and IndexerFlags no-op stub imports
// DROPPED (all Phase 15 Plan 15-12 STUB components returning null with zero
// render output); JSX render sites below collapsed to plain inline displays.
// Series/Series rewritten to Manga/Manga peer.
import Chapter from 'Chapter/Chapter';
import Icon from 'Components/Icon';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
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
import InteractiveImport from 'InteractiveImport/InteractiveImport';
import SelectLanguageModal from 'InteractiveImport/Language/SelectLanguageModal';
import SelectQualityModal from 'InteractiveImport/Quality/SelectQualityModal';
import SelectReleaseGroupModal from 'InteractiveImport/ReleaseGroup/SelectReleaseGroupModal';
import ReleaseType from 'InteractiveImport/ReleaseType';
import SelectReleaseTypeModal from 'InteractiveImport/ReleaseType/SelectReleaseTypeModal';
import SelectSeasonModal from 'InteractiveImport/Season/SelectSeasonModal';
import SelectMangaModal from 'InteractiveImport/Manga/SelectMangaModal';
import { useUpdateInteractiveImportItem } from 'InteractiveImport/useInteractiveImport';
import Language from 'Language/Language';
import Manga from 'Manga/Manga';
import { QualityModel } from 'Quality/Quality';
import CustomFormat from 'typings/CustomFormat';
import { SelectStateInputProps } from 'typings/props';
import Rejection from 'typings/Rejection';
import formatBytes from 'Utilities/Number/formatBytes';
import formatCustomFormatScore from 'Utilities/Number/formatCustomFormatScore';
import translate from 'Utilities/String/translate';
import InteractiveImportRowCellPlaceholder from './InteractiveImportRowCellPlaceholder';
import styles from './InteractiveImportRow.css';

type SelectType =
  | 'series'
  | 'season'
  | 'episode'
  | 'releaseGroup'
  | 'quality'
  | 'language'
  | 'indexerFlags'
  | 'releaseType';

type SelectedChangeProps = SelectStateInputProps & {
  hasEpisodeFileId: boolean;
};

interface InteractiveImportRowProps {
  id: number;
  allowSeriesChange: boolean;
  relativePath: string;
  series?: Manga;
  seasonNumber?: number;
  episodes?: Chapter[];
  releaseGroup?: string;
  quality?: QualityModel;
  languages?: Language[];
  size: number;
  releaseType: ReleaseType;
  customFormats?: CustomFormat[];
  customFormatScore?: number;
  indexerFlags: number;
  rejections: Rejection[];
  columns: Column[];
  episodeFileId?: number;
  isReprocessing?: boolean;
  modalTitle: string;
  onReprocessItems: (ids: number[]) => void;
  onSelectedChange(result: SelectedChangeProps): void;
  onValidRowChange(id: number, isValid: boolean): void;
}

function InteractiveImportRow(props: InteractiveImportRowProps) {
  const {
    id,
    allowSeriesChange,
    relativePath,
    series,
    seasonNumber,
    episodes = [],
    quality,
    languages,
    releaseGroup,
    size,
    releaseType,
    customFormats = [],
    customFormatScore,
    indexerFlags,
    rejections,
    isReprocessing,
    modalTitle,
    episodeFileId,
    columns,
    onReprocessItems,
    onSelectedChange,
    onValidRowChange,
  } = props;

  const { useIsSelected } = useSelect<InteractiveImport>();
  const isSelected = useIsSelected(id);
  const { updateInteractiveImportItem } = useUpdateInteractiveImportItem();

  const isSeriesColumnVisible = useMemo(
    () => columns.find((c) => c.name === 'series')?.isVisible ?? false,
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
      if (
        allowSeriesChange &&
        series &&
        seasonNumber != null &&
        episodes.length &&
        quality &&
        languages &&
        size > 0
      ) {
        onSelectedChange({
          id,
          hasEpisodeFileId: !!episodeFileId,
          value: true,
          shiftKey: false,
        });
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    []
  );

  useEffect(() => {
    const isValid = !!(
      series &&
      seasonNumber != null &&
      episodes.length &&
      quality &&
      languages
    );

    if (isSelected && !isValid) {
      onValidRowChange(id, false);
    } else {
      onValidRowChange(id, true);
    }
  }, [
    id,
    series,
    seasonNumber,
    episodes,
    quality,
    languages,
    isSelected,
    onValidRowChange,
  ]);

  const handleSelectedChange = useCallback(
    (result: SelectStateInputProps) => {
      onSelectedChange({
        ...result,
        hasEpisodeFileId: !!episodeFileId,
      });
    },
    [episodeFileId, onSelectedChange]
  );

  const selectRowAfterChange = useCallback(() => {
    if (!isSelected) {
      onSelectedChange({
        id,
        hasEpisodeFileId: !!episodeFileId,
        value: true,
        shiftKey: false,
      });
    }
  }, [id, episodeFileId, isSelected, onSelectedChange]);

  const onSelectModalClose = useCallback(() => {
    setSelectModalOpen(null);
  }, [setSelectModalOpen]);

  const onSelectSeriesPress = useCallback(() => {
    setSelectModalOpen('series');
  }, [setSelectModalOpen]);

  const onSeriesSelect = useCallback(
    (series: Manga) => {
      updateInteractiveImportItem(id, {
        series,
        seasonNumber: undefined,
        episodes: [],
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

  const onSelectSeasonPress = useCallback(() => {
    setSelectModalOpen('season');
  }, [setSelectModalOpen]);

  const onSeasonSelect = useCallback(
    (seasonNumber: number) => {
      updateInteractiveImportItem(id, {
        seasonNumber,
        episodes: [],
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

  const onSelectEpisodePress = useCallback(() => {
    setSelectModalOpen('episode');
  }, [setSelectModalOpen]);

  const onEpisodesSelect = useCallback(
    (selectedEpisodes: SelectedChapter[]) => {
      const episodes = selectedEpisodes[0].episodes;
      updateInteractiveImportItem(id, { episodes });
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

  const onSelectReleaseGroupPress = useCallback(() => {
    setSelectModalOpen('releaseGroup');
  }, [setSelectModalOpen]);

  const onReleaseGroupSelect = useCallback(
    (releaseGroup: string) => {
      updateInteractiveImportItem(id, { releaseGroup });
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

  const onSelectQualityPress = useCallback(() => {
    setSelectModalOpen('quality');
  }, [setSelectModalOpen]);

  const onQualitySelect = useCallback(
    (quality: QualityModel) => {
      updateInteractiveImportItem(id, { quality });
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

  const onSelectLanguagePress = useCallback(() => {
    setSelectModalOpen('language');
  }, [setSelectModalOpen]);

  const onLanguagesSelect = useCallback(
    (languages: Language[]) => {
      updateInteractiveImportItem(id, { languages });
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
    (releaseType: ReleaseType) => {
      updateInteractiveImportItem(id, { releaseType });
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
    (indexerFlags: number) => {
      updateInteractiveImportItem(id, { indexerFlags });
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

  const seriesTitle = series ? series.title : '';
  // Sonarr divergence: Phase 17.3 D-13/D-14 — dropped isAnime
  // (series?.seriesType === 'anime') local + the anime-format JSX branch +
  // the isAnime prop pass to SelectChapterModal (manga has no anime-format;
  // seriesType removed from Manga.ts per D-13). Plan 25-04 Task 1 — symbol
  // renamed alongside the InteractiveImport/Episode/ → /Chapter/ subdir move.

  // Sonarr divergence: Phase 17.3 Plan 17.3-13b — Episode/Episode rewritten
  // to Chapter/Chapter peer. The TV-shape `episodeNumber` field is preserved
  // at runtime (backend TV InteractiveImport flow is gated for v1 per Phase
  // 12 Plan 12-11 LOCK guard); cast through Chapter & { episodeNumber?: number }
  // keeps tsc happy. v1.x cleanup: collapse with the chapter-shape
  // InteractiveImport flow when the discriminator ships (Plan 12-98 v1.1
  // roadmap).
  const episodeInfo = episodes.map(
    (episode: Chapter & { episodeNumber?: number }) => {
      return (
        <div key={episode.id}>
          {episode.episodeNumber}

          {` - ${episode.title}`}
        </div>
      );
    }
  );

  const requiresSeasonNumber = isNaN(Number(seasonNumber));
  const showSeriesPlaceholder = isSelected && !series;
  const showSeasonNumberPlaceholder =
    isSelected && !!series && requiresSeasonNumber && !isReprocessing;
  const showEpisodeNumbersPlaceholder =
    isSelected && Number.isInteger(seasonNumber) && !episodes.length;
  const showReleaseGroupPlaceholder = isSelected && !releaseGroup;
  const showQualityPlaceholder = isSelected && !quality;
  const showLanguagePlaceholder = isSelected && !languages;
  const showIndexerFlagsPlaceholder = isSelected && !indexerFlags;

  // Phase 18 Plan-08 D-18 -- per-row testid keyed by item id.
  //
  // Testid shapes emitted at runtime (literal patterns for source-grep audits):
  //   interactive-import-row-{id}
  //   interactive-import-row-{id}-file
  //   interactive-import-row-{id}-manga
  //   interactive-import-row-{id}-chapter
  //   interactive-import-row-{id}-quality
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

      {isSeriesColumnVisible ? (
        <TableRowCellButton
          isDisabled={!allowSeriesChange}
          title={
            allowSeriesChange ? translate('ClickToChangeManga') : undefined
          }
          data-testid={`${rowTestId}-manga`}
          onPress={onSelectSeriesPress}
        >
          {showSeriesPlaceholder ? (
            <InteractiveImportRowCellPlaceholder />
          ) : (
            seriesTitle
          )}
        </TableRowCellButton>
      ) : null}

      <TableRowCellButton
        isDisabled={!series}
        title={series ? translate('ClickToChangeChapter') : undefined}
        onPress={onSelectSeasonPress}
      >
        {showSeasonNumberPlaceholder ? (
          <InteractiveImportRowCellPlaceholder />
        ) : (
          seasonNumber
        )}

        {isReprocessing && seasonNumber == null ? (
          <LoadingIndicator className={styles.reprocessing} size={20} />
        ) : null}
      </TableRowCellButton>

      <TableRowCellButton
        isDisabled={!series || requiresSeasonNumber}
        title={
          series && !requiresSeasonNumber
            ? translate('ClickToChangeChapter')
            : undefined
        }
        data-testid={`${rowTestId}-chapter`}
        onPress={onSelectEpisodePress}
      >
        {showEpisodeNumbersPlaceholder ? (
          <InteractiveImportRowCellPlaceholder />
        ) : (
          episodeInfo
        )}
      </TableRowCellButton>

      <TableRowCellButton
        title={translate('ClickToChangeReleaseGroup')}
        onPress={onSelectReleaseGroupPress}
      >
        {showReleaseGroupPlaceholder ? (
          <InteractiveImportRowCellPlaceholder isOptional={true} />
        ) : (
          releaseGroup
        )}
      </TableRowCellButton>

      {/* Sonarr divergence: Phase 17.3 Plan 17.3-13b — EpisodeQuality no-op
          stub dropped (zero render output); display quality.name directly. */}
      <TableRowCellButton
        className={styles.quality}
        title={translate('ClickToChangeQuality')}
        data-testid={`${rowTestId}-quality`}
        onPress={onSelectQualityPress}
      >
        {showQualityPlaceholder && <InteractiveImportRowCellPlaceholder />}

        {!showQualityPlaceholder && !!quality && (
          <span className={styles.label}>{quality.quality.name}</span>
        )}
      </TableRowCellButton>

      {/* Sonarr divergence: Phase 17.3 Plan 17.3-13b — EpisodeLanguages no-op
          stub dropped (zero render output); join language names directly. */}
      <TableRowCellButton
        className={styles.languages}
        title={translate('ClickToChangeLanguage')}
        onPress={onSelectLanguagePress}
      >
        {showLanguagePlaceholder && <InteractiveImportRowCellPlaceholder />}

        {!showLanguagePlaceholder && !!languages && (
          <span className={styles.label}>
            {languages.map((l) => l.name).join(', ')}
          </span>
        )}
      </TableRowCellButton>

      <TableRowCell>{formatBytes(size)}</TableRowCell>

      {/* Sonarr divergence: Phase 17.3 Plan 17.3-13b — getReleaseTypeName
          no-op stub dropped (returned translate('Unknown')); display
          releaseType string directly. */}
      <TableRowCellButton
        title={translate('ClickToChangeReleaseType')}
        onPress={onSelectReleaseTypePress}
      >
        {releaseType ?? translate('Unknown')}
      </TableRowCellButton>

      <TableRowCell>
        {/* Sonarr divergence: Phase 17.3 Plan 17.3-13b — EpisodeFormats no-op
            stub dropped (zero render output); display custom-format-score
            inline without Popover body. */}
        {customFormats?.length
          ? formatCustomFormatScore(customFormatScore, customFormats.length)
          : null}
      </TableRowCell>

      {/* Sonarr divergence: Phase 17.3 Plan 17.3-13b — IndexerFlags no-op
          stub dropped (zero render output); FLAG icon retained as meaningful
          signal when indexerFlags!==0. */}
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
        isOpen={selectModalOpen === 'series'}
        modalTitle={modalTitle}
        onSeriesSelect={onSeriesSelect}
        onModalClose={onSelectModalClose}
      />

      <SelectSeasonModal
        isOpen={selectModalOpen === 'season'}
        seriesId={series?.id}
        modalTitle={modalTitle}
        onSeasonSelect={onSeasonSelect}
        onModalClose={onSelectModalClose}
      />

      <SelectChapterModal
        isOpen={selectModalOpen === 'episode'}
        selectedIds={[id]}
        seriesId={series?.id}
        seasonNumber={seasonNumber}
        selectedDetails={relativePath}
        modalTitle={modalTitle}
        onEpisodesSelect={onEpisodesSelect}
        onModalClose={onSelectModalClose}
      />

      <SelectReleaseGroupModal
        isOpen={selectModalOpen === 'releaseGroup'}
        releaseGroup={releaseGroup ?? ''}
        modalTitle={modalTitle}
        onReleaseGroupSelect={onReleaseGroupSelect}
        onModalClose={onSelectModalClose}
      />

      <SelectQualityModal
        isOpen={selectModalOpen === 'quality'}
        qualityId={quality ? quality.quality.id : 0}
        proper={quality ? quality.revision.version > 1 : false}
        real={quality ? quality.revision.real > 0 : false}
        modalTitle={modalTitle}
        onQualitySelect={onQualitySelect}
        onModalClose={onSelectModalClose}
      />

      <SelectLanguageModal
        isOpen={selectModalOpen === 'language'}
        languageIds={languages ? languages.map((l) => l.id) : []}
        modalTitle={modalTitle}
        onLanguagesSelect={onLanguagesSelect}
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
