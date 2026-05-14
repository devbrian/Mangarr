// Sonarr divergence: NEW manga sibling per Phase 7 D-01 / D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/AddSeries/AddNewSeries/AddNewSeries.tsx
// (143-line verbatim port).
//
// Manga sibling preserves: search-grid layout, debounced lookup, side-panel
// modal pattern, Sonarr Page* component shell.
// Manga sibling diverges from AddNewSeries:
//   * Lookup hits /api/v5/manga/lookup (NOT /api/v3/series/lookup or
//     /api/v5/series/lookup).
//   * Search input placeholder: 'Search MangaDex...' (UI-SPEC §AddManga
//     Layout Contract).
//   * Empty-state copy uses MangaSearchPreEmpty / MangaSearchPreEmptyHint
//     i18n keys (Plan 11 lands en.json values; translate() falls back
//     gracefully on the key string).
//   * Modal content uses manga form fields per Phase 7 D-04.
//   * No "Import Existing" CTA — Import Lists are PROJECT.md Out-of-Scope
//     (Lock #12 — single-add only).
//
// Phase 8 cleanup: when AddSeries/ deletes, this becomes the canonical
// AddNew page; route /add/new can be retired.
import React, { useCallback, useEffect, useRef, useState } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import useDebounce from 'Helpers/Hooks/useDebounce';
import useQueryParams from 'Helpers/Hooks/useQueryParams';
import { icons, kinds } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import AddNewMangaSearchResult from './AddNewMangaSearchResult';
import { useLookupManga } from './useAddManga';
import styles from './AddNewManga.css';

function AddNewManga() {
  const { term: initialTerm = '' } = useQueryParams<{ term: string }>();
  const [term, setTerm] = useState(initialTerm);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const [isFetching, setIsFetching] = useState(false);
  // UI-SPEC §AddManga Layout Contract — debounce 500ms (Sonarr ships 300ms;
  // manga uses 500ms because MangaDex lookups are slower than TVDB).
  const query = useDebounce(term, term ? 500 : 0);

  const handleSearchInputChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setTerm(value);
      setIsFetching(!!value.trim());
    },
    []
  );

  const handleClearMangaLookupPress = useCallback(() => {
    setTerm('');
    setIsFetching(false);
    searchInputRef.current?.focus();
  }, []);

  const { isFetching: isFetchingApi, error, data } = useLookupManga(query);

  useEffect(() => {
    setIsFetching(isFetchingApi);
  }, [isFetchingApi]);

  useEffect(() => {
    setTerm(initialTerm);
  }, [initialTerm]);

  return (
    <PageContent title={translate('AddNewManga')}>
      <PageContentBody>
        <div className={styles.searchContainer} data-testid="add-manga-page">
          <div className={styles.searchIconContainer}>
            <Icon name={icons.SEARCH} size={20} />
          </div>

          <TextInput
            ref={searchInputRef}
            className={styles.searchInput}
            name="mangaLookup"
            value={term}
            placeholder={translate('SearchMangaDex')}
            autoFocus={true}
            data-testid="add-manga-search-input"
            onChange={handleSearchInputChange}
          />

          <Button
            className={styles.clearLookupButton}
            data-testid="add-manga-search-clear-button"
            onPress={handleClearMangaLookupPress}
          >
            <Icon name={icons.REMOVE} size={20} />
          </Button>
        </div>

        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && !!error ? (
          <div className={styles.message}>
            <div className={styles.helpText}>
              {translate('AddNewMangaError')}
            </div>

            <Alert kind={kinds.DANGER}>{getErrorMessage(error)}</Alert>
          </div>
        ) : null}

        {!isFetching && !error && !!data.length ? (
          <div className={styles.searchResults}>
            {data.map((item, index) => {
              // Lookup rows may not yet carry a database id; key on a stable
              // identifier from MangaDex / AniList / MAL when present, else
              // fall back to the position in the result list.
              const key =
                item.mangaDexId ??
                (item.aniListId != null ? `al-${item.aniListId}` : null) ??
                (item.malId != null ? `mal-${item.malId}` : null) ??
                `result-${index}`;

              return <AddNewMangaSearchResult key={key} manga={item} />;
            })}
          </div>
        ) : null}

        {!isFetching && !error && !data.length && term ? (
          <div className={styles.message}>
            <div className={styles.noResults}>
              {translate('MangaSearchNoMatchesFound')}
            </div>
            <div>{translate('MangaSearchNoMatchesFoundHint')}</div>
          </div>
        ) : null}

        {term ? null : (
          <div className={styles.message}>
            <div className={styles.helpText}>
              {translate('MangaSearchPreEmpty')}
            </div>
            <div>{translate('MangaSearchPreEmptyHint')}</div>
          </div>
        )}

        <div />
      </PageContentBody>
    </PageContent>
  );
}

export default AddNewManga;
