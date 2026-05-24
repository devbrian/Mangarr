// Sonarr divergence: Phase 15 Plan 15-12 STUB body replaced per Phase 30
// Plan 30-03 (II2-01) — see DIVERGENCE.md. Role-match analog: Sonarr
// v5-develop frontend/src/InteractiveImport/Episode/
// SelectEpisodeModalContent.tsx with aggressive R-5 strip:
//
//   * Episode -> Chapter; useEpisodes -> useChaptersByManga.
//   * Columns episodeNumber/title/airDate -> chapterNumber/title/releaseDate.
//   * Sort: TV-shape ordinal subtraction -> chapter-number subtraction.
//   * DROPPED Sonarr Season ordinal prop (no Season per PROJECT.md DOMAIN-02).
//   * DROPPED Sonarr anime-format prop (no scene-numbering in manga).
//   * DROPPED Sonarr multi-episode-per-file + startingIndex slice loop —
//     manga is 1 CBZ = 1 chapter (Phase 4 ARCHIVE invariant). The mapped
//     array reduces to a plain 1:1 selectedIds.map((id, index) =>
//     ({ id, chapters: [sortedChapters[index]] })).
//
// SelectedChapter interface at the BOTTOM of this file is the Plan 30-01
// Task 1 (II2-04) manga-shape rename target and stays untouched here.
//
// Pitfall 4 (PATTERNS.md): ZERO TV-shape (Series / Episode / Season)
// testid families in this file — only the manga-shape
// select-chapter-modal prefix is used.
import React, { useCallback, useMemo, useState } from 'react';
import Chapter from 'Chapter/Chapter';
import { useChaptersByManga } from 'Chapter/useChapter';
import CheckInput from 'Components/Form/CheckInput';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Scroller from 'Components/Scroller/Scroller';
import { kinds, scrollDirections, sizes } from 'Helpers/Props';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './SelectChapterModalContent.css';

interface SelectChapterModalContentProps {
  selectedIds: number[] | string[];
  mangaId?: number;
  selectedDetails?: string;
  modalTitle: string;
  onChaptersSelect?(selectedChapters: SelectedChapter[]): void;
  onModalClose(): void;
}

function SelectChapterModalContent(props: SelectChapterModalContentProps) {
  const {
    selectedIds,
    mangaId,
    selectedDetails,
    modalTitle,
    onChaptersSelect,
    onModalClose,
  } = props;

  // Plan 25-04 Task 4 + Plan 30-01 Task 1: useChaptersByManga is the manga
  // peer of Sonarr's useEpisodes hook (TV-shape series+season scoped).
  // Returns Chapter[] for the picked manga (mangaId comes from the
  // InteractiveImport row's previously-selected manga cell). Disabled
  // (returns DEFAULT_CHAPTERS=[]) when mangaId is undefined.
  const {
    data: chapters,
    isFetching,
    isFetched: isPopulated,
  } = useChaptersByManga(mangaId);

  const [selectedChapterIds, setSelectedChapterIds] = useState<Set<number>>(
    new Set<number>()
  );

  const sortedChapters = useMemo(() => {
    return [...chapters].sort((a, b) => a.chapterNumber - b.chapterNumber);
  }, [chapters]);

  const allSelected = useMemo(() => {
    return (
      sortedChapters.length > 0 &&
      sortedChapters.every((c) => selectedChapterIds.has(c.id))
    );
  }, [sortedChapters, selectedChapterIds]);

  const onSelectAllToggle = useCallback(() => {
    if (allSelected) {
      setSelectedChapterIds(new Set<number>());
    } else {
      setSelectedChapterIds(new Set(sortedChapters.map((c) => c.id)));
    }
  }, [allSelected, sortedChapters]);

  const onRowCheckedChange = useCallback(
    ({ name, value }: CheckInputChanged) => {
      const chapterId = parseInt(name);
      setSelectedChapterIds((prev) => {
        const next = new Set(prev);
        if (value) {
          next.add(chapterId);
        } else {
          next.delete(chapterId);
        }
        return next;
      });
    },
    []
  );

  const onSubmitPress = useCallback(() => {
    if (!onChaptersSelect) {
      onModalClose();
      return;
    }
    // R-5 strip: NO Sonarr multi-episode-per-file / startingIndex slice loop.
    // Manga is 1 CBZ = 1 chapter (Phase 4 ARCHIVE invariant). The 1:1
    // mapping packs the chosen Chapter[] into the SelectedChapter shape
    // each selected row consumes (per
    // InteractiveImportRow.onChaptersSelect at Plan 25-04 Task 4 :229-241).
    const pickedChapters = sortedChapters.filter((c) =>
      selectedChapterIds.has(c.id)
    );
    // Sonarr peer maps over selectedIds (the InteractiveImport row ids the
    // modal was opened for). For manga the row-to-chapter relationship is
    // 1:1; every selected row receives the SAME picked chapters list (the
    // row may consume just selectedChapters[0]?.chapters per Plan 30-01
    // Task 1 SelectedChapter shape).
    const payload: SelectedChapter[] = selectedIds.map((id) => ({
      id: typeof id === 'number' ? id : parseInt(id),
      chapters: pickedChapters,
      chapterNumber: pickedChapters[0]?.chapterNumber,
      title: pickedChapters[0]?.title,
    }));
    onChaptersSelect(payload);
  }, [
    onChaptersSelect,
    onModalClose,
    sortedChapters,
    selectedChapterIds,
    selectedIds,
  ]);

  const hasChapters = sortedChapters.length > 0;
  const canSubmit = selectedChapterIds.size > 0;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {modalTitle ? `${modalTitle} - ` : ''}
        {translate('SelectChapter')}
        {selectedDetails ? ` (${selectedDetails})` : ''}
      </ModalHeader>

      <ModalBody scrollDirection={scrollDirections.NONE}>
        <div data-testid="select-chapter-modal">
          {isFetching ? <LoadingIndicator /> : null}

          {!isFetching && isPopulated && !hasChapters ? (
            <div
              className={styles.empty}
              data-testid="select-chapter-modal-empty"
            >
              {translate('NoChaptersFound')}
            </div>
          ) : null}

          {!isFetching && hasChapters ? (
            <>
              <div className={styles.actionRow}>
                <Button
                  className={styles.selectAllButton}
                  size={sizes.SMALL}
                  data-testid="select-chapter-modal-select-all"
                  onPress={onSelectAllToggle}
                >
                  {allSelected
                    ? translate('UnselectAll')
                    : translate('SelectAll')}
                </Button>
              </div>

              <Scroller scrollDirection={scrollDirections.VERTICAL}>
                {sortedChapters.map((chapter) => {
                  return (
                    <div
                      key={chapter.id}
                      className={styles.chapterRow}
                      data-testid={`select-chapter-modal-row-${chapter.id}`}
                    >
                      <div className={styles.checkboxColumn}>
                        <CheckInput
                          name={chapter.id.toString()}
                          value={selectedChapterIds.has(chapter.id)}
                          onChange={onRowCheckedChange}
                        />
                      </div>
                      <div className={styles.chapterNumber}>
                        {chapter.chapterNumber}
                      </div>
                      <div className={styles.title}>{chapter.title ?? ''}</div>
                      <div className={styles.releaseDate}>
                        {chapter.firstReleaseDate ?? ''}
                      </div>
                    </div>
                  );
                })}
              </Scroller>
            </>
          ) : null}
        </div>
      </ModalBody>

      <ModalFooter>
        <Button
          data-testid="select-chapter-modal-cancel"
          onPress={onModalClose}
        >
          {translate('Cancel')}
        </Button>

        <Button
          kind={kinds.SUCCESS}
          isDisabled={!canSubmit}
          data-testid="select-chapter-modal-submit"
          onPress={onSubmitPress}
        >
          {translate('Select')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default SelectChapterModalContent;

// Plan 30-01 Task 1 (II2-04) manga-shape rename target:
// episodes -> chapters, episodeNumber -> chapterNumber, Season ordinal
// field DROPPED. Plan 30-03 body above consumes this shape — DO NOT rename back
// to TV-shape; the discriminator union in InteractiveImport.ts narrows on
// this shape downstream.
export interface SelectedChapter {
  id: number;
  title?: string;
  chapters?: Chapter[];
  chapterNumber?: number;
}
