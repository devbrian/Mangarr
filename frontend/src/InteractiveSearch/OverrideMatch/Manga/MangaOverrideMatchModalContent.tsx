// Sonarr divergence: NEW manga sibling per Phase 12 Plan 12-10 sub-wave-B-addition (audit row C closure) — see DIVERGENCE.md.
// Role-match analog: frontend/src/InteractiveSearch/OverrideMatch/OverrideMatchModalContent.tsx (TV-shape sibling — preserved verbatim per D-12-18).
// Source: .planning/phases/08-tv-manga-parity-audit/audit/OverrideMatch-vs-MangaPayloadRouting.md ## Backfill outline item 2.
//
// Manga sibling preserves: ModalContent + ModalHeader + ModalBody + ModalFooter scaffold; DescriptionList layout; OverrideMatchData per-row "selected value + change-button" presenter (the TV component is shape-neutral once the row data is manga-shaped); grab + cancel button row; useGrabMangaRelease error rendering at the bottom of the body; close-on-grab-success effect via usePrevious(isGrabbing); TV CSS module reuse (../OverrideMatchModalContent.css).
//
// Manga sibling diverges from OverrideMatchModalContent:
//   * Props are manga-shaped: { mangaId, chapterIds, scanlationGroup?, translatedLanguage?, downloadClientId?, protocol, onModalClose, indexerId, guid, title } — no seriesId / seasonNumber / episodes / languages / quality.
//   * State variables mirror manga shape (no useSingleSeries call; no episode-list state; no quality / languages state).
//   * Sub-modals: ONLY <SelectDownloadClientModal>. NO SelectSeriesModal / SelectSeasonModal / SelectEpisodeModal / SelectQualityModal / SelectLanguageModal — those are TV-only (audit gap-01.4).
//   * Grab payload: { override: { mangaId, chapterIds, downloadClientId, scanlationGroup?, translatedLanguage? } } — no episodeIds / quality / languages.
//   * Dispatched via useGrabMangaRelease (POSTs to /manga/release per getGrabPath discriminator) — NOT useGrabRelease (which hard-codes /release).
//   * No grab-validator gating — chapterIds + mangaId arrive populated from MangaReleaseResource (backend); empty-array guard on chapterIds preserves the UX symmetry with TV's "no episode selected" affordance.
//
// Phase 15 cleanup: when TV-side OverrideMatchModalContent is deleted, this manga sibling renames + flattens to OverrideMatch/OverrideMatchModalContent.tsx as the canonical default; useGrabRelease + OverrideRelease deletion happens in the same Phase 15 cascade.
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
import usePrevious from 'Helpers/Hooks/usePrevious';
import { useGrabMangaRelease } from 'InteractiveSearch/useReleases';
import { fetchDownloadClients } from 'Store/Actions/settingsActions';
import createEnabledDownloadClientsSelector from 'Store/Selectors/createEnabledDownloadClientsSelector';
import translate from 'Utilities/String/translate';
import SelectDownloadClientModal from '../DownloadClient/SelectDownloadClientModal';
import OverrideMatchData from '../OverrideMatchData';
import styles from '../OverrideMatchModalContent.css';

type SelectType = 'select' | 'downloadClient';

export interface MangaOverrideMatchModalContentProps {
  indexerId: number;
  title: string;
  guid: string;
  mangaId: number;
  chapterIds: number[];
  scanlationGroup?: string;
  translatedLanguage?: string;
  downloadClientId?: number | null;
  protocol: DownloadProtocol;
  onModalClose(): void;
}

function MangaOverrideMatchModalContent(
  props: MangaOverrideMatchModalContentProps
) {
  const modalTitle = translate('ManualGrab');
  const {
    title,
    indexerId,
    guid,
    mangaId,
    chapterIds,
    scanlationGroup,
    translatedLanguage,
    protocol,
    onModalClose,
  } = props;

  const [downloadClientId, setDownloadClientId] = useState<number | null>(
    props.downloadClientId ?? null
  );
  const [error, setError] = useState<string | null>(null);
  const [selectModalOpen, setSelectModalOpen] = useState<SelectType | null>(
    null
  );

  const { grabRelease, isGrabbing, grabError } = useGrabMangaRelease();
  const previousIsGrabbing = usePrevious(isGrabbing);

  const dispatch = useDispatch();
  const { items: downloadClients } = useSelector(
    createEnabledDownloadClientsSelector(protocol)
  );

  const chapterCount = useMemo(() => chapterIds.length, [chapterIds]);

  const onSelectModalClose = useCallback(() => {
    setSelectModalOpen(null);
  }, [setSelectModalOpen]);

  const onSelectDownloadClientPress = useCallback(() => {
    setSelectModalOpen('downloadClient');
  }, [setSelectModalOpen]);

  const onDownloadClientSelect = useCallback(
    (selected: number) => {
      setDownloadClientId(selected);
      setSelectModalOpen(null);
    },
    [setDownloadClientId, setSelectModalOpen]
  );

  const onGrabPress = useCallback(() => {
    // Phase 12 REVIEW MED-02 — sentinel-aware validation. InteractiveSearchRow.tsx:380-394
    // documents two valid payload shapes the manga override modal accepts:
    //   * ChapterSearchPayload: mangaId=0 (sentinel), chapterIds=[N] (non-empty)
    //   * MangaSearchPayload:   mangaId=N (real id), chapterIds=[] (empty — backend
    //                           resolves chapters from the cached RemoteChapter)
    // The backend MangaReleaseController.DownloadRelease keys off the cached
    // RemoteChapter (indexerId + guid), so mangaId / chapterIds are informational on
    // their respective sentinel paths and cache resolution drives the actual grab.
    //
    // The previous `if (!mangaId)` guard treated 0 as JS-falsy and surfaced a misleading
    // 'OverrideGrabNoSeries' error before the POST fired, locking the chapter-search
    // override-grab flow even though the backend would accept it. The corrected guard
    // rejects only when BOTH cache-keying fields are missing (genuinely no identifier
    // for the backend to resolve) — accepting either sentinel-mangaId or empty-chapterIds
    // alone, but not both.
    //
    // Phase 15 cleanup: when v1.1-04 splits this modal into ChapterOverrideMatchModal +
    // MangaOverrideMatchModal siblings, the sentinel disappears and this guard tightens
    // back to per-modal `if (!mangaId)` / `if (!chapterIds.length)` shapes.
    if (!mangaId && !chapterIds.length) {
      setError(translate('OverrideGrabNoChapter'));
      return;
    }

    grabRelease({
      indexerId,
      guid,
      override: {
        mangaId,
        chapterIds,
        downloadClientId,
        ...(scanlationGroup ? { scanlationGroup } : {}),
        ...(translatedLanguage ? { translatedLanguage } : {}),
      },
    });
  }, [
    indexerId,
    guid,
    mangaId,
    chapterIds,
    downloadClientId,
    scanlationGroup,
    translatedLanguage,
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
            title={translate('Release')}
            data={title}
          />

          <DescriptionListItem
            className={styles.item}
            title={translate('Chapters')}
            data={chapterCount}
          />

          {/* DownloadClient picker — the only TV sub-modal that is shape-neutral.
              Manga grab override surface is intentionally minimal: chapter/manga
              IDs are already known from the search context; only download-client
              picking is a useful override on the manga path (audit gap-01.4). */}
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

export default MangaOverrideMatchModalContent;
