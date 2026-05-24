import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import DownloadProtocol from 'DownloadClient/DownloadProtocol';
// Sonarr divergence: Phase 17.3 Plan 17.3-13b (D-09 stub-importer cascade) —
// Episode/EpisodeLanguages + Episode/EpisodeQuality no-op stub imports DROPPED
// (both Phase 15 Plan 15-12 STUB components returning null with zero render
// output); JSX render sites below collapsed to plain quality.name / language
// names listings. Series/Series + Series/useSeries (useSingleSeries) rewritten
// to Manga/Manga + Manga/useManga (useSingleManga) peers.
import usePrevious from 'Helpers/Hooks/usePrevious';
import SelectChapterModal from 'InteractiveImport/Chapter/SelectChapterModal';
// Phase 31 D-11 (IL2-06) — SelectedChapter import restored alongside the
// onEpisodesSelect useCallback (Plan 30-01 II2-05 deletion reverted; the
// callback's consumer-side adapter at lines below maps the modal's emitted
// SelectedChapter[] to ReleaseEpisode[] per RESEARCH §Item 3 — no modal
// contract retrofit needed, Plan 30-03 anticipated this restore).
import { SelectedChapter } from 'InteractiveImport/Chapter/SelectChapterModalContent';
import SelectLanguageModal from 'InteractiveImport/Language/SelectLanguageModal';
import SelectMangaModal from 'InteractiveImport/Manga/SelectMangaModal';
import SelectQualityModal from 'InteractiveImport/Quality/SelectQualityModal';
import { ReleaseEpisode, useGrabRelease } from 'InteractiveSearch/useReleases';
import Language from 'Language/Language';
import Manga from 'Manga/Manga';
import { useSingleManga } from 'Manga/useManga';
import { QualityModel } from 'Quality/Quality';
import { fetchDownloadClients } from 'Store/Actions/settingsActions';
import createEnabledDownloadClientsSelector from 'Store/Selectors/createEnabledDownloadClientsSelector';
import translate from 'Utilities/String/translate';
import SelectDownloadClientModal from './DownloadClient/SelectDownloadClientModal';
import OverrideMatchData from './OverrideMatchData';
import styles from './OverrideMatchModalContent.css';

// Plan 25-04 Task 3 — 'season' variant dropped (manga has no season per
// DOMAIN-02). The OverrideMatchModalContent retains the TV-only carry-over
// (this is the TV-shape fallback consumed when searchPayload.kind is
// episode/season per Task 5's discriminator branch); the 'season' state
// transitions are no longer reachable.
type SelectType =
  | 'select'
  | 'series'
  | 'episode'
  | 'quality'
  | 'language'
  | 'downloadClient';

export interface OverrideMatchModalContentProps {
  indexerId: number;
  title: string;
  guid: string;
  seriesId?: number;
  seasonNumber?: number;
  episodes: ReleaseEpisode[];
  languages: Language[];
  quality: QualityModel;
  protocol: DownloadProtocol;
  isGrabbing: boolean;
  grabError?: string;
  grabRelease: ReturnType<typeof useGrabRelease>['grabRelease'];
  onModalClose(): void;
}

function OverrideMatchModalContent(props: OverrideMatchModalContentProps) {
  const modalTitle = translate('ManualGrab');
  const {
    indexerId,
    title,
    guid,
    protocol,
    isGrabbing,
    grabError,
    grabRelease,
    onModalClose,
  } = props;

  // Phase 31 D-11 (IL2-06) — restore Sonarr-canonical useState wire-through.
  // Plan 30-01 Task 2 (II2-05) collapsed these to props-destructure reads
  // based on the THEN-true observation that SelectMangaModal +
  // SelectChapterModal returned `null` stubs (no selections could ever
  // arrive). Plan 30-03 (II2-01) shipped real modal bodies that emit
  // selections — the wire-through is meaningful again. The `seasonNumber`
  // carry-over remains a non-mutable TV-fallback (manga has no season per
  // DOMAIN-02; the field is consumed by the existing JSX guard at
  // OverrideMatchModalContent.tsx:264 `isNaN(Number(seasonNumber))` which
  // disables the Chapter trigger on the TV fallback path).
  const [seriesId, setSeriesId] = useState<number | undefined>(props.seriesId);
  const seasonNumber = props.seasonNumber;
  const [episodes, setEpisodes] = useState<ReleaseEpisode[]>(props.episodes);
  const [languages, setLanguages] = useState(props.languages);
  const [quality, setQuality] = useState(props.quality);
  const [downloadClientId, setDownloadClientId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectModalOpen, setSelectModalOpen] = useState<SelectType | null>(
    null
  );
  const previousIsGrabbing = usePrevious(isGrabbing);

  const dispatch = useDispatch();
  const series: Manga | undefined = useSingleManga(seriesId);
  const { items: downloadClients } = useSelector(
    createEnabledDownloadClientsSelector(protocol)
  );

  // Sonarr divergence: Phase 17.3 D-13/D-14 — dropped
  // `series?.seriesType === 'anime'` anime-format branch (manga has no
  // anime-format; seriesType removed from Manga.ts per D-13).
  const episodeInfo = useMemo(() => {
    return episodes.map((episode) => {
      return (
        <div key={episode.id}>
          {episode.episodeNumber}

          {` - ${episode.title}`}
        </div>
      );
    });
  }, [episodes]);

  const onSelectModalClose = useCallback(() => {
    setSelectModalOpen(null);
  }, [setSelectModalOpen]);

  const onSelectSeriesPress = useCallback(() => {
    setSelectModalOpen('series');
  }, [setSelectModalOpen]);

  // Plan 25-04 Task 3 — onSelectSeasonPress + onSeasonSelect dropped
  // alongside SelectSeasonModal (manga has no season per DOMAIN-02; Season/
  // subdir deleted in same commit). seasonNumber state remains for TV
  // fallback consumer typing; it is no longer user-mutable here.

  // Phase 31 D-11 (IL2-06) — restore Sonarr-canonical onSeriesSelect /
  // onEpisodesSelect callbacks. Plan 30-03 (II2-01) shipped SelectMangaModal
  // + SelectChapterModal bodies that emit selections; these callbacks wire
  // the emitted shape into local useState.
  //
  // onMangaSelect emits the full Manga object (SelectMangaModalContent.tsx:41)
  // — adapter takes manga.id for setSeriesId. PR #262 Codex P2 fix: when
  // the user picks a NEW manga, clear `episodes` so a subsequent Grab can't
  // submit chapter IDs that belong to the previously selected manga (the
  // prior behavior dropped dependent selection on series change; restoring
  // that here closes the cross-manga stale-chapter-submission window).
  //
  // onChaptersSelect emits SelectedChapter[] (SelectChapterModalContent.tsx:135-148).
  // PR #262 Codex P1 fix: project from `c.chapters` (the user's picker
  // selection — full Chapter objects), NOT `c.id` (which is the CALLER's
  // row id, i.e. the prior selectedIds we seeded the modal with). The bug
  // was that mapping `c.id` -> ReleaseEpisode.id copies the seed back into
  // the override grab payload, so the user's new chapter pick never reaches
  // the wire. SelectChapterModalContent writes the SAME picker selection
  // into every row's `chapters[]` (1 CBZ = 1 chapter, R-5 manga shape), so
  // we take the first row's chapters and project each Chapter -> ReleaseEpisode.
  const onSeriesSelect = useCallback(
    (manga: Manga) => {
      // PR #262 Codex P2 + CodeRabbit Minor follow-up: only clear chapter
      // selection when the manga ACTUALLY changes. Re-selecting the current
      // manga should not erase a valid in-progress override. Cross-manga
      // chapter IDs still can't leak into the next Grab because the picker
      // would need a different manga.id to even reach this branch.
      if (manga.id !== seriesId) {
        setEpisodes([]);
      }
      setSeriesId(manga.id);
      setSelectModalOpen(null);
    },
    [seriesId, setSeriesId, setEpisodes, setSelectModalOpen]
  );

  const onEpisodesSelect = useCallback(
    (selectedChapters: SelectedChapter[]) => {
      // PR #262 Codex P1: project from c.chapters (picker selection), not c.id
      // (caller's prior seed). The picker writes the same chapters[] into every
      // row; the first row carries the full selection. episodeFileId + seasonNumber
      // default to 0 (no chapter-file id is known until import; manga has no
      // season per DOMAIN-02).
      const pickedChapters = selectedChapters[0]?.chapters ?? [];
      setEpisodes(
        pickedChapters.map((ch) => ({
          id: ch.id,
          episodeFileId: 0,
          seasonNumber: 0,
          episodeNumber: ch.chapterNumber ?? 0,
          title: ch.title ?? '',
        }))
      );
      setSelectModalOpen(null);
    },
    [setEpisodes, setSelectModalOpen]
  );

  const onSelectEpisodePress = useCallback(() => {
    setSelectModalOpen('episode');
  }, [setSelectModalOpen]);

  const onSelectQualityPress = useCallback(() => {
    setSelectModalOpen('quality');
  }, [setSelectModalOpen]);

  const onQualitySelect = useCallback(
    (quality: QualityModel) => {
      setQuality(quality);
      setSelectModalOpen(null);
    },
    [setQuality, setSelectModalOpen]
  );

  const onSelectLanguagesPress = useCallback(() => {
    setSelectModalOpen('language');
  }, [setSelectModalOpen]);

  const onLanguagesSelect = useCallback(
    (languages: Language[]) => {
      setLanguages(languages);
      setSelectModalOpen(null);
    },
    [setLanguages, setSelectModalOpen]
  );

  const onSelectDownloadClientPress = useCallback(() => {
    setSelectModalOpen('downloadClient');
  }, [setSelectModalOpen]);

  const onDownloadClientSelect = useCallback(
    (downloadClientId: number) => {
      setDownloadClientId(downloadClientId);
      setSelectModalOpen(null);
    },
    [setDownloadClientId, setSelectModalOpen]
  );

  const onGrabPress = useCallback(() => {
    if (!seriesId) {
      setError(translate('OverrideGrabNoManga'));
      return;
    } else if (!episodes.length) {
      setError(translate('OverrideGrabNoChapter'));
      return;
    } else if (!quality) {
      setError(translate('OverrideGrabNoQuality'));
      return;
    } else if (!languages.length) {
      setError(translate('OverrideGrabNoLanguage'));
      return;
    }

    grabRelease({
      indexerId,
      guid,
      override: {
        seriesId,
        episodeIds: episodes.map((e) => e.id),
        quality,
        languages,
        downloadClientId,
      },
    });
  }, [
    indexerId,
    guid,
    seriesId,
    episodes,
    quality,
    languages,
    downloadClientId,
    setError,
    grabRelease,
  ]);

  useEffect(() => {
    if (!isGrabbing && previousIsGrabbing) {
      onModalClose();
    }
  }, [isGrabbing, previousIsGrabbing, onModalClose]);

  useEffect(
    () => {
      dispatch(fetchDownloadClients());
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    []
  );

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('OverrideGrabModalTitle', { title })}
      </ModalHeader>

      <ModalBody>
        <DescriptionList>
          <DescriptionListItem
            className={styles.item}
            // Sonarr divergence: Phase 17.3 Plan 17.3-16 (D-04) — user-visible
            // title swapped from translate('Series') to translate('Manga'); bare
            // "Series" i18n key was deleted by Phase 15-07, leaving this callsite
            // orphan. Pre-existing carry-forward fix-forward.
            title={translate('Manga')}
            data={
              <OverrideMatchData
                value={series?.title}
                onPress={onSelectSeriesPress}
              />
            }
          />

          {/* Plan 25-04 Task 3 — SeasonNumber DescriptionListItem dropped
              (manga has no season per DOMAIN-02). */}

          <DescriptionListItem
            className={styles.item}
            title={translate('Chapters')}
            data={
              <OverrideMatchData
                value={episodeInfo}
                isDisabled={!series || isNaN(Number(seasonNumber))}
                onPress={onSelectEpisodePress}
              />
            }
          />

          {/* Sonarr divergence: Phase 17.3 Plan 17.3-13b — EpisodeQuality
              no-op stub dropped (zero render output); display quality.name
              directly. */}
          <DescriptionListItem
            className={styles.item}
            title={translate('Quality')}
            data={
              <OverrideMatchData
                value={quality?.quality?.name ?? ''}
                onPress={onSelectQualityPress}
              />
            }
          />

          {/* Sonarr divergence: Phase 17.3 Plan 17.3-13b — EpisodeLanguages
              no-op stub dropped (zero render output); join language names
              directly. */}
          <DescriptionListItem
            className={styles.item}
            title={translate('Languages')}
            data={
              <OverrideMatchData
                value={languages.map((l) => l.name).join(', ')}
                onPress={onSelectLanguagesPress}
              />
            }
          />

          {downloadClients.length > 1 ? (
            <DescriptionListItem
              className={styles.item}
              title={translate('DownloadClient')}
              data={
                <OverrideMatchData
                  value={
                    downloadClients.find(
                      (downloadClient) => downloadClient.id === downloadClientId
                    )?.name ?? translate('Default')
                  }
                  onPress={onSelectDownloadClientPress}
                />
              }
            />
          ) : null}
        </DescriptionList>
      </ModalBody>

      <ModalFooter className={styles.footer}>
        <div className={styles.error}>{error || grabError}</div>

        <div className={styles.buttons}>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <SpinnerErrorButton
            isSpinning={isGrabbing}
            error={grabError}
            onPress={onGrabPress}
          >
            {translate('GrabRelease')}
          </SpinnerErrorButton>
        </div>
      </ModalFooter>

      {/* Phase 31 D-11 (IL2-06) — wire SelectMangaModal.onMangaSelect ->
          onSeriesSelect. Plan 30-03 SelectMangaModal exposes onMangaSelect
          (optional per the anticipatory wrapper comment at
          SelectMangaModal.tsx:18-25). The local useState restored above is
          the consumer of this wire. */}
      <SelectMangaModal
        isOpen={selectModalOpen === 'series'}
        modalTitle={modalTitle}
        onMangaSelect={onSeriesSelect}
        onModalClose={onSelectModalClose}
      />

      {/* Plan 25-04 Task 3 — SelectSeasonModal JSX dropped (manga has no
          season per DOMAIN-02; Season/ subdir deleted in same commit). */}

      {/* Phase 31 D-11 (IL2-06) — wire SelectChapterModal.onChaptersSelect
          -> onEpisodesSelect. The consumer-side adapter inside
          onEpisodesSelect maps SelectedChapter[] -> ReleaseEpisode[] per
          RESEARCH §Item 3 (no modal contract retrofit needed — Plan 30-03
          anticipated this restore via the optional wrapper callback at
          SelectChapterModal.tsx:28-37). The mangaId prop continues to use
          the local seriesId state per the Phase 30 D-01 / CONTEXT.md
          `<deferred>` "OverrideMatchModalContent broader refactor" scope
          carve-out.

          Phase 31 fix-forward (REVIEW.md §WR-03 remediation, 2026-05-24):
          selectedIds now passes the locally-mutable `episodes` state's
          numeric `id` field, NOT the release GUID. The previous shape
          passed a [guid] array through SelectChapterModalContent.onSubmitPress
          where parseInt(id) returns NaN (filtered → empty payload →
          user-facing "no chapter" error) or a partial-digit-prefix parse
          that fabricates a numeric ID unrelated to any real chapter row.
          Passing numeric episode IDs preserves the selection-state
          fidelity through the picker. */}
      <SelectChapterModal
        isOpen={selectModalOpen === 'episode'}
        selectedIds={episodes.map((e) => e.id)}
        mangaId={seriesId}
        selectedDetails={title}
        modalTitle={modalTitle}
        onChaptersSelect={onEpisodesSelect}
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

      <SelectDownloadClientModal
        isOpen={selectModalOpen === 'downloadClient'}
        protocol={protocol}
        modalTitle={modalTitle}
        onDownloadClientSelect={onDownloadClientSelect}
        onModalClose={onSelectModalClose}
      />
    </ModalContent>
  );
}

export default OverrideMatchModalContent;
