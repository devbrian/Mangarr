// Sonarr divergence: NEW manga sibling per Phase 7 D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/AddSeries/AddNewSeries/AddNewSeriesModalContent.tsx
// (the side-panel form scaffolded verbatim with manga form-field divergence).
//
// Manga sibling preserves: ModalContent layout, Form/FormGroup/FormInputGroup
// component shell, selectSettings + getValidationFailures plumbing,
// SpinnerButton submit, ModalFooter SearchOnAdd row.
// Manga sibling diverges from AddNewSeriesModalContent (per D-04 + Phase 6 D-03/D-06):
//   * Drops the TV-only series-type field (anime/standard/daily — manga has
//     no equivalent; PROJECT.md MangaType is a separate concept reserved for
//     future plans).
//   * Drops the season-folder field (Volumes/Seasons OoS per PROJECT.md).
//   * Drops the cutoff-unmet search toggle (manga has no cutoff concept yet).
//   * Replaces the Sonarr quality-profile selector with a translationProfileId
//     selector reading from /api/v5/translationprofile (Phase 5 D-01).
//   * Adds customFormatProfileId reading from /api/v5/customformatprofile
//     (Phase 5 D-07) — adjacent to TranslationProfile.
//   * Renames the missing-search toggle to searchForMissingChapters
//     (Phase 6 D-06 SearchOnAdd toggle).
//   * Monitor dropdown ships the 5 manga values per Phase 6 D-03 + UI-SPEC
//     §Form / monitor labels (NOT the 11 Sonarr-side entries).
//
// Phase 42 Plan 42-07: the <Form> body + profile-fallback plumbing was factored
// into the shared <AddMangaFormBody> (reused by the count-only bulk-add modal).
// This component keeps the single-add ModalContent shell — poster/overview +
// the footer SearchOnAdd toggle + the "Add {title}" SpinnerButton — and reads
// the SAME addMangaOptionsStore for the submit payload + root-folder gate.
//
// Phase 8 cleanup: collapse with AddNewSeriesModalContent when AddSeries/ deletes.
import React, { useCallback } from 'react';
import { AddMangaResult } from 'AddManga/AddManga';
import {
  setAddMangaOption,
  useAddMangaOptions,
} from 'AddManga/addMangaOptionsStore';
import { useAppDimension } from 'App/appStore';
import CheckInput from 'Components/Form/CheckInput';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import MangaPoster from 'Manga/MangaPoster';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import AddMangaFormBody from './AddMangaFormBody';
import { useAddManga } from './useAddManga';
import styles from './AddNewMangaModalContent.css';

export interface AddNewMangaModalContentProps {
  manga: AddMangaResult;
  onModalClose: () => void;
}

function AddNewMangaModalContent({
  manga,
  onModalClose,
}: AddNewMangaModalContentProps) {
  const { title, year, overview, images } = manga;
  const options = useAddMangaOptions();
  const isSmallScreen = useAppDimension('isSmallScreen');

  const { isAdding, addError, addManga } = useAddManga();

  // Bug fix pr-smoke-add-manga-timeout (2026-05-14): RootFolderSelectInput
  // replaces the zustand store's default `rootFolderPath: ''` with the first
  // real root folder only inside useEffects gated on the async useRootFolders()
  // query. If the user (or an automation flow) clicks Add before that query
  // resolves, the POST /api/v5/manga fires with rootFolderPath: '' and the
  // backend PathValidator rejects the bare title with HTTP 400 — the modal then
  // correctly stays open, but the add never lands. Gate the submit on a
  // populated root folder; mirrors how Sonarr's AddNewSeries gated submit on a
  // chosen root folder. Read straight from the shared store (the form body
  // writes through the same store).
  const isRootFolderMissing = !options.rootFolderPath;

  const handleSearchToggleChange = useCallback(
    ({ value }: CheckInputChanged) => {
      // searchForMissingChapters is the only field the footer toggle owns; the
      // form body owns the rest. Both write through addMangaOptionsStore.
      setAddMangaOption('searchForMissingChapters', value);
    },
    []
  );

  const handleAddMangaPress = useCallback(() => {
    // Bug fix pr-smoke-add-manga-timeout (2026-05-14): defense-in-depth guard —
    // never POST while the root folder is still unpopulated (see
    // isRootFolderMissing above). The SpinnerButton is also disabled in this
    // state, but guarding the handler too closes any window where a press lands
    // before the disabled state has rendered.
    if (isRootFolderMissing) {
      console.error(
        'AddNewMangaModalContent: handleAddMangaPress invoked with an empty rootFolderPath — the Add-button isDisabled gating has regressed.'
      );
      return;
    }

    // Bug fix new-manga-default-monitored (2026-05-08): send `monitored: true`
    // explicitly + nest the per-Chapter Monitor cascade fields under `addOptions`
    // so they survive backend MangaResource → Manga.AddOptions deserialization.
    addManga({
      title: manga.title,
      titleSlug: manga.titleSlug,
      mangaBakaId: manga.mangaBakaId,
      mangaDexId: manga.mangaDexId,
      aniListId: manga.aniListId,
      malId: manga.malId,
      rootFolderPath: options.rootFolderPath,
      monitored: options.monitor !== 'none',
      monitor: options.monitor,
      addOptions: {
        monitor: options.monitor,
        searchForMissingChapters: options.searchForMissingChapters,
      },
      translationProfileId: options.translationProfileId,
      customFormatProfileId: options.customFormatProfileId,
      tags: options.tags,
      searchForMissingChapters: options.searchForMissingChapters,
    });
  }, [manga, isRootFolderMissing, options, addManga]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {title}

        {!title.includes(String(year)) && year ? (
          <span className={styles.year}>({year})</span>
        ) : null}
      </ModalHeader>

      <ModalBody>
        <div className={styles.container} data-testid="add-manga-modal">
          {isSmallScreen ? null : (
            <div className={styles.poster}>
              <MangaPoster
                className={styles.poster}
                images={images}
                size={250}
                title={title}
              />
            </div>
          )}

          <div className={styles.info}>
            {overview ? (
              <div className={styles.overview}>{overview}</div>
            ) : null}

            <AddMangaFormBody addError={addError} rootFolderName={title} />
          </div>
        </div>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div>
          <label className={styles.searchLabelContainer}>
            <span className={styles.searchLabel}>
              {translate('AddNewMangaSearchForMissingChapters')}
            </span>

            <CheckInput
              containerClassName={styles.searchInputContainer}
              className={styles.searchInput}
              name="searchForMissingChapters"
              value={options.searchForMissingChapters}
              onChange={handleSearchToggleChange}
            />
          </label>
        </div>

        <SpinnerButton
          className={styles.addButton}
          kind={kinds.SUCCESS}
          isSpinning={isAdding}
          isDisabled={isRootFolderMissing}
          data-testid="add-manga-modal-add-button"
          onPress={handleAddMangaPress}
        >
          {translate('AddMangaWithTitle', { title })}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddNewMangaModalContent;
