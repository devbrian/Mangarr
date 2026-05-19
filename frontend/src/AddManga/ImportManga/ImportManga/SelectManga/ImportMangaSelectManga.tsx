// Sonarr divergence: NEW manga sibling per debug-add-import-ui-mismatch fix
// (2026-05-19). Role-match analog:
// frontend/src/AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesSelectSeries.tsx.
//
// Manga sibling preserves: floating-ui popover (`@floating-ui/react`) +
// debounced text input + scrollable results list + LoadingIndicator
// while fetching + warning-icon when no match found / search failed +
// "Existing" badge when the auto-match collides with an already-imported
// manga.
// Manga sibling diverges:
//   - useLookupManga (existing hook) instead of useLookupSeries
//   - mangaDexId (string) replaces tvdbId (number) as identifier
//   - Mangarr's importMangaStore exports a slightly different action
//     surface (no `useIsCurrentedItemQueued`); the spinner gate is the
//     simpler `isCurrentLookupItem || isFetching` predicate
//   - metadataSource label replaces Sonarr's `network` label
//
// Phase 8 cleanup: collapse with ImportSeriesSelectSeries when AddSeries/
// deletes.
import {
  autoUpdate,
  flip,
  FloatingPortal,
  useClick,
  useDismiss,
  useFloating,
  useInteractions,
} from '@floating-ui/react';
import React, { useCallback, useEffect, useState } from 'react';
import { useLookupManga } from 'AddManga/AddNewManga/useAddManga';
import FormInputButton from 'Components/Form/FormInputButton';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import useDebounce from 'Helpers/Hooks/useDebounce';
import { icons, kinds } from 'Helpers/Props';
import useExistingManga from 'Manga/useExistingManga';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import {
  addToLookupQueue,
  updateImportMangaItem,
  useImportMangaItem,
  useIsCurrentLookupQueueItem,
} from '../../importMangaStore';
import ImportMangaSearchResult from './ImportMangaSearchResult';
import ImportMangaTitle from './ImportMangaTitle';
import styles from './ImportMangaSelectManga.css';

interface ImportMangaSelectMangaProps {
  id: string;
}

interface MetadataSourceCandidate {
  mangaDexId?: string;
  aniListId?: number;
  malId?: number;
}

function getMetadataSourceLabel(
  candidate: MetadataSourceCandidate | undefined
): string | undefined {
  if (!candidate) {
    return undefined;
  }
  if (candidate.mangaDexId) {
    return 'MangaDex';
  }
  if (candidate.aniListId) {
    return 'AniList';
  }
  if (candidate.malId) {
    return 'MyAnimeList';
  }
  return undefined;
}

function ImportMangaSelectManga({ id }: ImportMangaSelectMangaProps) {
  const importMangaItem = useImportMangaItem(id);
  const { selectedManga, name } = importMangaItem ?? {};
  const isExistingManga = useExistingManga(selectedManga?.mangaDexId);

  const [term, setTerm] = useState(name ?? '');
  const [isOpen, setIsOpen] = useState(false);
  const query = useDebounce(term, term ? 300 : 0);
  const isCurrentLookupQueueItem = useIsCurrentLookupQueueItem(id);

  const { isFetching, isFetched, error, data, refetch } = useLookupManga(
    query,
    isCurrentLookupQueueItem
  );

  const errorMessage = getErrorMessage(error);
  const isLookingUpManga = isFetching || isCurrentLookupQueueItem;

  const metadataSource = getMetadataSourceLabel(selectedManga);

  const handlePress = useCallback(() => {
    setIsOpen((prevIsOpen) => !prevIsOpen);
  }, []);

  const handleSearchInputChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setTerm(value);
      addToLookupQueue(id);
    },
    [id]
  );

  const handleRefreshPress = useCallback(() => {
    refetch();
  }, [refetch]);

  const handleMangaSelect = useCallback(
    (mangaDexId: string | undefined) => {
      setIsOpen(false);

      const next = data.find((item) => item.mangaDexId === mangaDexId);
      if (!next) {
        return;
      }

      updateImportMangaItem(id, {
        selectedManga: next,
        hasSearched: true,
      });
    },
    [id, data]
  );

  // NOTE: The auto-select-top-match-on-first-lookup logic lives in
  // ImportMangaRow (not here). Duplicating it in this dropdown caused a
  // setState→effect→setState infinite loop, because updateImportMangaItem
  // changes the item reference which is in the deps array. The dropdown
  // only fires updateImportMangaItem in handleMangaSelect (user click).

  useEffect(() => {
    if (name != null) {
      setTerm(name);
    }
  }, [name]);

  const { refs, context, floatingStyles } = useFloating({
    middleware: [
      flip({
        crossAxis: false,
        mainAxis: true,
      }),
    ],
    open: isOpen,
    placement: 'bottom',
    whileElementsMounted: autoUpdate,
    onOpenChange: setIsOpen,
  });

  const click = useClick(context);
  const dismiss = useDismiss(context);

  const { getReferenceProps, getFloatingProps } = useInteractions([
    click,
    dismiss,
  ]);

  return (
    <>
      <div ref={refs.setReference} {...getReferenceProps()}>
        <Link className={styles.button} component="div" onPress={handlePress}>
          {isLookingUpManga && !isFetched ? (
            <LoadingIndicator className={styles.loading} size={20} />
          ) : null}

          {isFetched && selectedManga && isExistingManga ? (
            <Icon
              className={styles.warningIcon}
              name={icons.WARNING}
              kind={kinds.WARNING}
            />
          ) : null}

          {isFetched && selectedManga ? (
            <ImportMangaTitle
              title={selectedManga.title}
              year={selectedManga.year ?? selectedManga.publicationYear}
              metadataSource={metadataSource}
              isExistingManga={isExistingManga}
            />
          ) : null}

          {isFetched && !selectedManga ? (
            <div>
              <Icon
                className={styles.warningIcon}
                name={icons.WARNING}
                kind={kinds.WARNING}
              />

              {translate('NoMatchFound')}
            </div>
          ) : null}

          {!isFetching && !!error ? (
            <div>
              <Icon
                className={styles.warningIcon}
                title={errorMessage}
                name={icons.WARNING}
                kind={kinds.WARNING}
              />

              {translate('SearchFailedError')}
            </div>
          ) : null}

          <div className={styles.dropdownArrowContainer}>
            <Icon name={icons.CARET_DOWN} />
          </div>
        </Link>
      </div>

      {isOpen ? (
        <FloatingPortal id="portal-root">
          <div
            ref={refs.setFloating}
            className={styles.contentContainer}
            style={floatingStyles}
            {...getFloatingProps()}
          >
            <div className={styles.content}>
              <div className={styles.searchContainer}>
                <div className={styles.searchIconContainer}>
                  <Icon name={icons.SEARCH} />
                </div>

                <TextInput
                  className={styles.searchInput}
                  name={`${name ?? id}_textInput`}
                  value={term}
                  onChange={handleSearchInputChange}
                />

                <FormInputButton
                  kind={kinds.DEFAULT}
                  spinnerIcon={icons.REFRESH}
                  canSpin={true}
                  isSpinning={isFetching}
                  onPress={handleRefreshPress}
                >
                  <Icon name={icons.REFRESH} />
                </FormInputButton>
              </div>

              <div className={styles.results}>
                {data.map((item) => {
                  // mangaDexId is the canonical row-key for the dropdown.
                  // Fall back to title in the rare case a result lacks
                  // mangaDexId (other identifiers may still be present).
                  const key = item.mangaDexId ?? item.title;
                  return (
                    <ImportMangaSearchResult
                      key={key}
                      mangaDexId={item.mangaDexId}
                      title={item.title}
                      year={item.year ?? item.publicationYear}
                      metadataSource={getMetadataSourceLabel(item)}
                      onPress={handleMangaSelect}
                    />
                  );
                })}
              </div>
            </div>
          </div>
        </FloatingPortal>
      ) : null}
    </>
  );
}

export default ImportMangaSelectManga;
