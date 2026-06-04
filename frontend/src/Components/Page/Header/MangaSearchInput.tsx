// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Components/Page/Header/SeriesSearchInput.tsx
// (the TV original deleted to a stub in Phase 15 Plan 15-12; the header search
// bar was deferred to "v1.1+ as MangaSearchInput"). This is that deferred port.
//
// Manga sibling preserves: the Autosuggest + fuse.worker fuzzy-search shape,
// the "Existing <domain>" / "Add New <domain>" two-section layout, the Tab/Enter
// navigate-to-first-result behaviour, and the focus-search keyboard shortcut.
// Manga sibling diverges from SeriesSearchInput:
//   * Sources from useManga() (queryKey ['/manga']) not useSeries().
//   * Matches manga metadata IDs (mangaDexId / aniListId / malId) not
//     tvdbId / tvMazeId / imdbId / tmdbId.
//   * Routes to `/manga/${titleSlug}` (Plan 07-04 D-09 detail route) and to
//     `/add/manga?term=` (AddNewManga reads the `term` query param).
//   * window.Mangarr.urlBase (not window.Sonarr).
import { push } from 'connected-react-router';
import { ExtendedKeyboardEvent } from 'mousetrap';
import React, {
  FormEvent,
  KeyboardEvent,
  SyntheticEvent,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import Autosuggest from 'react-autosuggest';
import { useDispatch } from 'react-redux';
import { useDebouncedCallback } from 'use-debounce';
import Icon from 'Components/Icon';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import useKeyboardShortcuts from 'Helpers/Hooks/useKeyboardShortcuts';
import { icons } from 'Helpers/Props';
import Manga from 'Manga/Manga';
import useManga from 'Manga/useManga';
import { Tag, useTagList } from 'Tags/useTags';
import translate from 'Utilities/String/translate';
import MangaSearchResult from './MangaSearchResult';
import styles from './MangaSearchInput.css';

const ADD_NEW_TYPE = 'addNew';

interface Match {
  key: string;
  refIndex: number;
}

interface AddNewMangaSuggestion {
  type: 'addNew';
  title: string;
}

export interface SuggestedManga
  extends Pick<
    Manga,
    | 'title'
    | 'sortTitle'
    | 'images'
    | 'alternateTitles'
    | 'mangaDexId'
    | 'aniListId'
    | 'malId'
  > {
  // Required here (Manga.titleSlug is optional) — both navigation paths build
  // /manga/${titleSlug}, so a slug-less record would route to /manga/undefined.
  // useMangaSuggestions filters those out, guaranteeing this is always present.
  titleSlug: string;
  firstCharacter: string;
  tags: Tag[];
}

interface MangaSuggestion {
  title: string;
  indices: number[];
  item: SuggestedManga;
  matches: Match[];
  refIndex: number;
}

interface Section {
  title: string;
  loading?: boolean;
  suggestions: MangaSuggestion[] | AddNewMangaSuggestion[];
}

function useMangaSuggestions(tagList: Tag[]) {
  const { data: allManga = [] } = useManga();

  return useMemo(() => {
    return allManga.flatMap((manga): SuggestedManga[] => {
      const {
        title,
        titleSlug,
        sortTitle,
        images,
        alternateTitles = [],
        mangaDexId,
        aniListId,
        malId,
        tags = [],
      } = manga;

      // Skip slug-less records so navigation never builds /manga/undefined.
      if (!titleSlug) {
        return [];
      }

      return [
        {
          title,
          titleSlug,
          sortTitle,
          images,
          alternateTitles,
          mangaDexId,
          aniListId,
          malId,
          firstCharacter: title.charAt(0).toLowerCase(),
          tags: tags.reduce<Tag[]>((acc, id) => {
            const matchingTag = tagList.find((tag) => tag.id === id);

            if (matchingTag) {
              acc.push(matchingTag);
            }

            return acc;
          }, []),
        },
      ];
    });
  }, [allManga, tagList]);
}

function MangaSearchInput() {
  const tagList = useTagList();
  const manga = useMangaSuggestions(tagList);
  const dispatch = useDispatch();
  const { bindShortcut, unbindShortcut } = useKeyboardShortcuts();

  const [value, setValue] = useState('');
  const [requestLoading, setRequestLoading] = useState(false);
  const [suggestions, setSuggestions] = useState<MangaSuggestion[]>([]);

  const autosuggestRef = useRef<Autosuggest>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const worker = useRef<Worker | null>(null);
  const isLoading = useRef(false);
  const requestValue = useRef<string | null>(null);

  const suggestionGroups = useMemo(() => {
    const result: Section[] = [];

    if (suggestions.length || requestLoading) {
      result.push({
        title: translate('ExistingManga'),
        loading: requestLoading,
        suggestions,
      });
    }

    result.push({
      title: translate('AddNewManga'),
      suggestions: [
        {
          type: ADD_NEW_TYPE,
          title: value,
        },
      ],
    });

    return result;
  }, [suggestions, value, requestLoading]);

  const handleSuggestionsReceived = useCallback(
    (message: { data: { value: string; suggestions: MangaSuggestion[] } }) => {
      const { value, suggestions } = message.data;

      if (!isLoading.current) {
        requestValue.current = null;
        setRequestLoading(false);
      } else if (value === requestValue.current) {
        setSuggestions(suggestions);
        requestValue.current = null;
        setRequestLoading(false);
        isLoading.current = false;
      } else {
        setSuggestions(suggestions);
        setRequestLoading(true);

        const payload = {
          value: requestValue.current,
          manga,
        };

        worker.current?.postMessage(payload);
      }
    },
    [manga]
  );

  const requestSuggestions = useDebouncedCallback((value: string) => {
    if (!isLoading.current) {
      return;
    }

    requestValue.current = value;
    setRequestLoading(true);

    if (!requestLoading) {
      const payload = {
        value,
        manga,
      };

      worker.current?.postMessage(payload);
    }
  }, 250);

  const reset = useCallback(() => {
    setValue('');
    setSuggestions([]);
    isLoading.current = false;
  }, []);

  const focusInput = useCallback((event: ExtendedKeyboardEvent) => {
    event.preventDefault();
    inputRef.current?.focus();
  }, []);

  const getSectionSuggestions = useCallback((section: Section) => {
    return section.suggestions;
  }, []);

  const renderSectionTitle = useCallback((section: Section) => {
    return (
      <div className={styles.sectionTitle}>
        {section.title}

        {section.loading && (
          <LoadingIndicator
            className={styles.loading}
            rippleClassName={styles.ripple}
            size={20}
          />
        )}
      </div>
    );
  }, []);

  const getSuggestionValue = useCallback(({ title }: { title: string }) => {
    return title;
  }, []);

  const renderSuggestion = useCallback(
    (
      item: AddNewMangaSuggestion | MangaSuggestion,
      { query }: { query: string }
    ) => {
      if ('type' in item) {
        return (
          <div className={styles.addNewMangaSuggestion}>
            {translate('SearchForQuery', { query })}
          </div>
        );
      }

      return <MangaSearchResult {...item.item} match={item.matches[0]} />;
    },
    []
  );

  const handleChange = useCallback(
    (
      _event: FormEvent<HTMLElement>,
      {
        newValue,
        method,
      }: {
        newValue: string;
        method: 'down' | 'up' | 'escape' | 'enter' | 'click' | 'type';
      }
    ) => {
      if (method === 'up' || method === 'down') {
        return;
      }

      setValue(newValue);
    },
    []
  );

  const handleKeyDown = useCallback(
    (event: KeyboardEvent<HTMLElement>) => {
      if (event.shiftKey || event.altKey || event.ctrlKey) {
        return;
      }

      if (event.key === 'Escape') {
        reset();
        return;
      }

      if (event.key !== 'Tab' && event.key !== 'Enter') {
        return;
      }

      if (!autosuggestRef.current) {
        return;
      }

      if (!inputRef.current?.value) {
        return;
      }

      const { highlightedSectionIndex, highlightedSuggestionIndex } =
        autosuggestRef.current.state;

      if (!suggestions.length || highlightedSectionIndex) {
        dispatch(
          push(
            `${window.Mangarr.urlBase}/add/manga?term=${encodeURIComponent(
              value
            )}`
          )
        );

        inputRef.current?.blur();
        reset();

        return;
      }

      // If a suggestion is not selected go to the first manga,
      // otherwise go to the selected manga.

      const selectedSuggestion =
        highlightedSuggestionIndex == null
          ? suggestions[0]
          : suggestions[highlightedSuggestionIndex];

      dispatch(
        push(
          `${window.Mangarr.urlBase}/manga/${selectedSuggestion.item.titleSlug}`
        )
      );

      inputRef.current?.blur();
      reset();
    },
    [value, suggestions, dispatch, reset]
  );

  const handleBlur = useCallback(() => {
    reset();
  }, [reset]);

  const handleSuggestionsFetchRequested = useCallback(
    ({ value }: { value: string }) => {
      isLoading.current = true;

      requestSuggestions(value);
    },
    [requestSuggestions]
  );

  const handleSuggestionsClearRequested = useCallback(() => {
    setSuggestions([]);
    isLoading.current = false;
  }, []);

  const handleSuggestionSelected = useCallback(
    (
      _event: SyntheticEvent,
      { suggestion }: { suggestion: MangaSuggestion | AddNewMangaSuggestion }
    ) => {
      if ('type' in suggestion) {
        dispatch(
          push(
            `${window.Mangarr.urlBase}/add/manga?term=${encodeURIComponent(
              value
            )}`
          )
        );
      } else {
        setValue('');
        dispatch(
          push(`${window.Mangarr.urlBase}/manga/${suggestion.item.titleSlug}`)
        );
      }
    },
    [value, dispatch]
  );

  const inputProps = {
    ref: inputRef,
    className: styles.input,
    name: 'mangaSearch',
    value,
    placeholder: translate('Search'),
    autoComplete: 'off',
    spellCheck: false,
    onChange: handleChange,
    onKeyDown: handleKeyDown,
    onBlur: handleBlur,
  };

  const theme = {
    container: styles.container,
    containerOpen: styles.containerOpen,
    suggestionsContainer: styles.mangaContainer,
    suggestionsList: styles.list,
    suggestion: styles.listItem,
    suggestionHighlighted: styles.highlighted,
  };

  useEffect(() => {
    worker.current = new Worker(new URL('./fuse.worker.ts', import.meta.url));

    return () => {
      if (worker.current) {
        worker.current.terminate();
        worker.current = null;
      }
    };
  }, []);

  useEffect(() => {
    worker.current?.addEventListener(
      'message',
      handleSuggestionsReceived,
      false
    );

    return () => {
      if (worker.current) {
        worker.current.removeEventListener(
          'message',
          handleSuggestionsReceived,
          false
        );
      }
    };
  }, [handleSuggestionsReceived]);

  useEffect(() => {
    bindShortcut('focusMangaSearchInput', focusInput);

    return () => {
      unbindShortcut('focusMangaSearchInput');
    };
  }, [bindShortcut, unbindShortcut, focusInput]);

  return (
    <div className={styles.wrapper}>
      <Icon name={icons.SEARCH} />

      <Autosuggest
        ref={autosuggestRef}
        inputProps={inputProps}
        theme={theme}
        focusInputOnSuggestionClick={false}
        multiSection={true}
        suggestions={suggestionGroups}
        getSectionSuggestions={getSectionSuggestions}
        renderSectionTitle={renderSectionTitle}
        getSuggestionValue={getSuggestionValue}
        renderSuggestion={renderSuggestion}
        onSuggestionSelected={handleSuggestionSelected}
        onSuggestionsFetchRequested={handleSuggestionsFetchRequested}
        onSuggestionsClearRequested={handleSuggestionsClearRequested}
      />
    </div>
  );
}

export default MangaSearchInput;
