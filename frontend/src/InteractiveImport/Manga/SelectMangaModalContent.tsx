// Sonarr divergence: NEW per Phase 30 Plan 30-03 (II2-01) — see DIVERGENCE.md.
// Role-match analog: Sonarr v5-develop
// `frontend/src/InteractiveImport/Series/SelectSeriesModalContent.tsx`
// (RESEARCH §3 + PATTERNS.md §Plan 30-03). Line-by-line port with:
//   * `Series` -> `Manga` type substitution.
//   * `useSeries()` -> `useManga()` (the manga library hook backed by
//     `useApiQuery<Manga[]>({ queryKey: ['/manga'] })`).
//   * Columns Sonarr [title / year / tvdbId / imdbId] -> Mangarr
//     [title / year / mangaDexId]; `imdbId` dropped (no manga peer);
//     `malId` deferred per UI-SPEC §Surface 1 (column clutter; v1.3+).
//   * Filter predicate: substring match on `title.toLowerCase()` OR
//     `mangaDexId` substring; no `imdbId` branch.
//   * STRIPPED Sonarr `monitoredOnly` filter — manga library does not
//     surface a per-row monitored filter in the per-cell picker.
//
// Replaces the Plan-15-12 / Plan-25-04 Task 2 `return null` stub at
// `SelectMangaModal.tsx`; the outer modal wrapper is in the same plan
// (this file is the inner Body + autocomplete picker).
//
// Pitfall 4 (PATTERNS.md): ZERO TV-shape (Series / Episode / Season)
// testid families in this file — only the manga-shape select-manga-modal
// prefix is used. Audit gate `scripts/audit-ui-inventory.sh` Gate 5
// enforces via source-text grep.
import React, { useCallback, useMemo, useState } from 'react';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Scroller from 'Components/Scroller/Scroller';
import { scrollDirections } from 'Helpers/Props';
import Manga from 'Manga/Manga';
import useManga from 'Manga/useManga';
import sortByProp from 'Utilities/Array/sortByProp';
import translate from 'Utilities/String/translate';
import styles from './SelectMangaModalContent.css';

interface SelectMangaModalContentProps {
  modalTitle: string;
  onMangaSelect(manga: Manga): void;
  onModalClose(): void;
}

function SelectMangaModalContent(props: SelectMangaModalContentProps) {
  const { modalTitle, onMangaSelect, onModalClose } = props;

  // useManga returns { data: Manga[], mangaMap, isFetching, isFetched, error }
  // — verified at frontend/src/Manga/useManga.ts:332-354. Backed by the
  // /api/v5/manga library endpoint which the user has already populated by
  // the time they reach the InteractiveImport flow (no library = nothing
  // to pick).
  const { data: allManga, isFetching, isFetched: isPopulated } = useManga();

  const [filter, setFilter] = useState('');

  const onFilterChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setFilter(event.target.value);
    },
    [setFilter]
  );

  const sortedManga = useMemo(() => {
    return [...allManga].sort(sortByProp('sortTitle'));
  }, [allManga]);

  const filteredManga = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    if (!needle) {
      return sortedManga;
    }
    return sortedManga.filter((manga) => {
      // Sonarr peer uses title.toLowerCase().includes(filter) || tvdbId.toString().includes(filter)
      // || imdbId?.includes(filter). Manga peer drops imdbId; mangaDexId is
      // optional on the wire shape but present on every library row by the
      // time it reaches this picker.
      const titleMatch = manga.title.toLowerCase().includes(needle);
      const mangaDexIdMatch =
        !!manga.mangaDexId && manga.mangaDexId.toLowerCase().includes(needle);
      return titleMatch || mangaDexIdMatch;
    });
  }, [sortedManga, filter]);

  const onMangaPress = useCallback(
    (manga: Manga) => () => {
      onMangaSelect(manga);
    },
    [onMangaSelect]
  );

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {modalTitle ? `${modalTitle} - ` : ''}
        {translate('SelectManga')}
      </ModalHeader>

      <ModalBody
        scrollDirection={scrollDirections.NONE}
        // data-testid is intentionally on ModalBody (the modal root content
        // surface) per UI-SPEC §Surface 1.
      >
        <div data-testid="select-manga-modal">
          <input
            className={styles.filterInput}
            type="text"
            name="filter"
            placeholder={translate('FilterMangaPlaceholder')}
            value={filter}
            autoFocus={true}
            data-testid="select-manga-modal-filter"
            onChange={onFilterChange}
          />

          {isFetching ? <LoadingIndicator /> : null}

          {!isFetching && isPopulated && filteredManga.length === 0 ? (
            <div
              className={styles.empty}
              data-testid="select-manga-modal-empty"
            >
              <div className={styles.emptyHeading}>
                {translate('NoMangaFound')}
              </div>
              <div className={styles.emptyHelp}>
                {translate('NoMangaFoundHelp')}
              </div>
            </div>
          ) : null}

          {!isFetching && filteredManga.length > 0 ? (
            <Scroller scrollDirection={scrollDirections.VERTICAL}>
              {filteredManga.map((manga) => {
                return (
                  <div
                    key={manga.id}
                    className={styles.mangaRow}
                    role="button"
                    tabIndex={0}
                    data-testid={`select-manga-modal-row-${manga.id}`}
                    onClick={onMangaPress(manga)}
                  >
                    <div className={styles.title}>{manga.title}</div>
                    <div className={styles.metadata}>
                      {manga.year ?? manga.publicationYear ?? ''}
                      {manga.mangaDexId ? ` · ${manga.mangaDexId}` : ''}
                    </div>
                  </div>
                );
              })}
            </Scroller>
          ) : null}
        </div>
      </ModalBody>

      <ModalFooter>
        <Button data-testid="select-manga-modal-cancel" onPress={onModalClose}>
          {translate('Cancel')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default SelectMangaModalContent;
