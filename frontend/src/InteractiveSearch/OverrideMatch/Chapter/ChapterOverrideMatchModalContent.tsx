// Sonarr divergence: NEW per Phase 25 Plan 25-04 Task 5 (v1.1-04
// OverrideMatch split) — see DIVERGENCE.md.
//
// Chapter-specific OverrideMatchModal content body — preserves the
// chapterIds-handling code paths from the pre-split MangaOverrideMatchModalContent.
// chapterIds REQUIRED + NON-EMPTY (no fallback to []). Whole-manga override
// is handled by the sibling MangaOverrideMatchModal flavor (narrowed in
// Task 6 to drop chapterIds entirely).
//
// 25-hotfix (UAT item 4 / WR-04): The wire-level `mangaId=0` sentinel is
// PRESERVED for chapter-flavored overrides — the caller (InteractiveSearchRow.tsx)
// passes `mangaId={0}` and `onGrabPress` below forwards that literal in the
// override payload. The typed split decommissions only the RUNTIME
// shape-detection ambiguity (no more "is chapterIds present?" property checks),
// not the wire-format sentinel. The `mangaId: number` prop type — rather than
// `number | null` — reflects this: 0 is the only valid value at the chapter
// call site and the backend `MangaReleaseController` accepts it as the
// documented chapter-flavored override shape. See ChapterOverrideMatchModal.tsx
// header + 25-REVIEW.md §WR-04 for the full disposition.
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

export interface ChapterOverrideMatchModalContentProps {
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

function ChapterOverrideMatchModalContent(
  props: ChapterOverrideMatchModalContentProps
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
    // Plan 25-04 Task 5 — chapterIds REQUIRED + NON-EMPTY for the chapter
    // flavor. The pre-split sentinel branch (mangaId=0 acceptable when
    // chapterIds non-empty) collapses to a strict chapterIds.length > 0
    // check because the type already guarantees mangaId is a real id.
    if (!chapterIds.length) {
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

export default ChapterOverrideMatchModalContent;
