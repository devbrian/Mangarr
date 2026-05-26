// Sonarr divergence: NEW manga sibling per Phase 12 Plan 12-10 sub-wave-B-addition (audit row C closure) — see DIVERGENCE.md.
// Source: .planning/phases/08-tv-manga-parity-audit/audit/OverrideMatch-vs-MangaPayloadRouting.md ## Backfill outline item 2.
// The TV-shape OverrideMatchModalContent sibling was retired in issue #263; the
// shared OverrideMatchData presenter + OverrideMatchModalContent.css survive it.
//
// This modal preserves: ModalContent + ModalHeader + ModalBody + ModalFooter scaffold; DescriptionList layout; OverrideMatchData per-row "selected value + change-button" presenter; grab + cancel button row; useGrabMangaRelease error rendering at the bottom of the body; close-on-grab-success effect via usePrevious(isGrabbing); shared CSS module reuse (../OverrideMatchModalContent.css).
//
// Manga shape:
//   * Props: { mangaId, scanlationGroup?, translatedLanguage?, downloadClientId?, protocol, onModalClose, indexerId, guid, title } — no seriesId / seasonNumber / episodes / languages / quality. Plan 25-04 Task 6 (v1.1-04 narrow) dropped the historical chapterIds prop entirely; the chapter-flavored search now uses the sibling ChapterOverrideMatchModal authored in Task 5.
//   * State variables mirror manga shape (no episode-list state; no quality / languages state).
//   * Sub-modals: ONLY <SelectDownloadClientModal>.
//   * Grab payload: { override: { mangaId, chapterIds, downloadClientId, scanlationGroup?, translatedLanguage? } } — no episodeIds / quality / languages.
//   * Dispatched via useGrabMangaRelease (POSTs to /manga/release).
//   * No grab-validator gating — chapterIds + mangaId arrive populated from MangaReleaseResource (backend); empty-array guard on chapterIds preserves the UX symmetry.
import React, { useCallback, useEffect, useState } from 'react';
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

// Plan 25-04 Task 6 (v1.1-04 OverrideMatch split narrow) — chapterIds
// prop dropped entirely. The pre-split MangaOverrideMatchModal carried
// chapterIds via sentinel logic (the chapter-flavored search routed
// through this same modal with empty mangaId); Task 5 split into a
// sibling ChapterOverrideMatchModal that owns the chapterIds flow.
// This sibling is now strictly the manga-flavored override (whole-manga
// grab; backend resolves the chapter list from the cached RemoteChapter
// keyed by indexerId + guid).
export interface MangaOverrideMatchModalContentProps {
  indexerId: number;
  title: string;
  guid: string;
  mangaId: number;
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

  // Plan 25-04 Task 6 — chapterCount memo dropped alongside chapterIds
  // prop. The manga-flavored override no longer renders a per-chapter
  // count (the backend resolves the chapter list from cache by
  // indexerId + guid).

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
    // Plan 25-04 Task 6 (v1.1-04 narrow) — the pre-split sentinel guard
    // (mangaId=0 + chapterIds non-empty acceptable) is decommissioned by
    // the typed-union split. This sibling is now strictly the manga-only
    // override; mangaId is REQUIRED. Backend resolves the chapter list
    // from the cached RemoteChapter (indexerId + guid).
    if (!mangaId) {
      setError(translate('OverrideGrabNoManga'));
      return;
    }

    grabRelease({
      indexerId,
      guid,
      override: {
        mangaId,
        chapterIds: [],
        downloadClientId,
        ...(scanlationGroup ? { scanlationGroup } : {}),
        ...(translatedLanguage ? { translatedLanguage } : {}),
      },
    });
  }, [
    indexerId,
    guid,
    mangaId,
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

          {/* Plan 25-04 Task 6 — Chapters DescriptionListItem dropped
              (sole consumer of the chapterIds prop, now removed). */}

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
