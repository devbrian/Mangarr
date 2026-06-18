// Sonarr divergence: NEW manga sibling per Phase 15 Plan 15-12 deferred
// "v1.1+ dedicated single-manga Edit modal" — referenced in
// frontend/src/Manga/Details/MangaDetails.tsx (Edit button toolbar wiring).
// Role-match analog: frontend/src/Series/Edit/EditSeriesModalContent.tsx
// (deleted in Plan 15-07; canonical reference is git commit 0521a6c39).
//
// Manga sibling preserves: ModalContent layout, Form/FormGroup/FormInputGroup
// component shell, usePendingChangesStore + selectSettings plumbing,
// SpinnerErrorButton submit.
//
// === Path edit + RootFolder picker + MoveManga confirmation (issue #81) ===
// Pre-issue-81 the Path field rendered read-only ("Path edit deferred") and
// the bulk-edit modal hard-wired moveFiles=false; Phase 17.3 Plan 17.3-03
// D-11 had deleted the upstream MoveSeriesModal stub before a real port
// could land. issue #81 ports the upstream EditSeriesModalContent.tsx
// path-edit pattern: clicking the root-folder button on the Path field
// opens RootFolderModal (the manga peer of upstream's RootFolderModal —
// hits GET /api/v5/manga/{id}/folder via MangaFolderController, which was
// shipped Phase 13 Plan 13-05 D-13-04 forward-prophylactic and now reaches
// its first caller). After the user picks a destination root, the Path
// FormInputGroup updates with the computed new path + the pending root.
// On Save: if path is changing, MoveMangaModal pops up with three options
// (Cancel / No Move / Yes Move). "Yes Move" routes useSaveManga with
// moveFiles=true which adds ?moveFiles=true to the PUT — backend
// MangaController.UpdateManga enqueues MoveMangaCommand BEFORE ApplyChanges
// so MoveMangaService sees the original on-disk path.
//
// === Editable field set (Issue #28 — 2026-05-09; #356 — 2026-06-09) ===
// The field-set fields below round-trip end-to-end through MangaResource +
// Manga.ApplyChanges. PR #27 originally shipped just Monitored + Tags;
// Issue #28 added TranslationProfileId / CustomFormatProfileId. #356 REMOVED
// the user-facing MonitorNewItems control — new-chapter monitoring is now
// derived from the Monitor choice at add/import time, so the Edit modal no
// longer exposes it.
//
// Phase 8 cleanup: this file IS the manga canonical now (Tv/ subtree gone).
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputButton from 'Components/Form/FormInputButton';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { usePendingChangesStore } from 'Helpers/Hooks/usePendingChangesStore';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { icons, inputTypes, kinds, sizes } from 'Helpers/Props';
import Manga from 'Manga/Manga';
import MoveMangaModal from 'Manga/MoveManga/MoveMangaModal';
import { useSaveManga, useSingleManga } from 'Manga/useManga';
import selectSettings from 'Store/Selectors/selectSettings';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import RootFolderModal from './RootFolder/RootFolderModal';
import { RootFolderUpdated } from './RootFolder/RootFolderModalContent';
import styles from './EditMangaModalContent.css';

export interface EditMangaModalContentProps {
  mangaId: number;
  onModalClose: () => void;
}

// Editable subset of the Manga record. Fields here MUST round-trip through
// MangaResource + Manga.ApplyChanges; verify on the backend before extending.
// `path` was added in issue #81 (was read-only pre-PR).
interface EditableMangaFields {
  monitored: boolean;
  translationProfileId: number;
  customFormatProfileId: number;
  path: string;
  tags: number[];
  // quick-260618-eqz — free-text user alternative titles (TEXT_TAG chip input).
  // Rides the existing {...manga, ...pendingChanges} save spread — no save-handler change.
  userAlternativeTitles: string[];
}

interface ProfileResource {
  id: number;
  name?: string;
}

// Inner form component: runs only when `manga` is defined (the wrapper
// short-circuits to null otherwise). Splitting this out keeps `selectSettings`
// strongly typed — the unconditional happy-path assignment lets each
// FormInputGroup spread receive the right `value: T[K]` shape.
interface EditMangaFormProps {
  manga: Manga;
  onModalClose: () => void;
}

function EditMangaForm({ manga, onModalClose }: EditMangaFormProps) {
  const { pendingChanges, setPendingChange } =
    usePendingChangesStore<EditableMangaFields>({});

  // issue #81: derive isPathChanging from pendingChanges.path vs manga.path.
  // The MoveManga gate only triggers when the user has actually picked a
  // new root folder via RootFolderModal AND the resulting path differs
  // from manga.path. isPathChanging drives both the useSaveManga
  // moveFiles arg AND the conditional MoveMangaModal pop-up gate.
  const isPathChanging = !!(
    pendingChanges.path && manga.path !== pendingChanges.path
  );

  const { saveManga, isSaving, saveError } = useSaveManga(
    manga.id,
    isPathChanging
  );
  const wasSaving = usePrevious(isSaving);

  const [isRootFolderModalOpen, setIsRootFolderModalOpen] = useState(false);
  const [rootFolderPath, setRootFolderPath] = useState(
    manga.rootFolderPath ?? ''
  );
  const [isConfirmMoveModalOpen, setIsConfirmMoveModalOpen] = useState(false);

  // Profile dropdowns reuse the Phase 5 V5 list endpoints. Mirrors the
  // AddNewMangaModalContent pattern — same useApiQuery + EnhancedSelectInputValue
  // mapping. Defer enabling until the modal is open (it always is when this
  // component renders); no debounce needed.
  const { data: translationProfilesData } = useApiQuery<ProfileResource[]>({
    path: '/translationprofile',
  });
  const { data: customFormatProfilesData } = useApiQuery<ProfileResource[]>({
    path: '/customformatprofile',
  });

  const translationProfileValues = useMemo<
    EnhancedSelectInputValue<number>[]
  >(() => {
    return (translationProfilesData ?? []).map((profile) => ({
      key: profile.id,
      value: profile.name ?? `Translation Profile ${profile.id}`,
    }));
  }, [translationProfilesData]);

  const customFormatProfileValues = useMemo<
    EnhancedSelectInputValue<number>[]
  >(() => {
    return (customFormatProfilesData ?? []).map((profile) => ({
      key: profile.id,
      value: profile.name ?? `Custom Format Profile ${profile.id}`,
    }));
  }, [customFormatProfilesData]);

  const initial = useMemo<EditableMangaFields>(
    () => ({
      monitored: manga.monitored,
      // 0 means "fall back to Config.DefaultTranslationProfileId / Config.
      // DefaultCustomFormatProfileId" (Phase 5 D-11 default-seeded entities).
      translationProfileId: manga.translationProfileId ?? 0,
      customFormatProfileId: manga.customFormatProfileId ?? 0,
      path: manga.path,
      tags: manga.tags,
      userAlternativeTitles: manga.userAlternativeTitles ?? [],
    }),
    [manga]
  );

  const { settings, validationErrors, validationWarnings } = useMemo(() => {
    return {
      ...selectSettings(initial, pendingChanges, saveError),
      ...getValidationFailures(saveError),
    };
  }, [initial, pendingChanges, saveError]);

  const {
    monitored,
    translationProfileId,
    customFormatProfileId,
    path,
    tags,
    userAlternativeTitles,
  } = settings;

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      // @ts-expect-error name is keyof EditableMangaFields at runtime
      setPendingChange(name, value);
    },
    [setPendingChange]
  );

  const handleRootFolderPress = useCallback(() => {
    setIsRootFolderModalOpen(true);
  }, []);

  const handleRootFolderModalClose = useCallback(() => {
    setIsRootFolderModalOpen(false);
  }, []);

  const handleRootFolderChange = useCallback(
    ({
      path: newPath,
      rootFolderPath: newRootFolderPath,
    }: RootFolderUpdated) => {
      setIsRootFolderModalOpen(false);
      setRootFolderPath(newRootFolderPath);
      handleInputChange({ name: 'path', value: newPath });
    },
    [handleInputChange]
  );

  const handleCancelPress = useCallback(() => {
    setIsConfirmMoveModalOpen(false);
  }, []);

  // Save handler: if the path is changing AND the confirm modal hasn't
  // been opened yet, open it. Otherwise (no path change, OR confirm modal
  // already open and the user clicked "No Move"), save with moveFiles
  // driven by the useSaveManga(mangaId, isPathChanging) hook closure.
  // The "Yes Move" path goes through handleMoveMangaPress.
  const handleSavePress = useCallback(() => {
    if (isPathChanging && !isConfirmMoveModalOpen) {
      setIsConfirmMoveModalOpen(true);
    } else {
      setIsConfirmMoveModalOpen(false);

      saveManga({
        ...manga,
        ...pendingChanges,
        // rootFolderPath is a derived state — must be passed alongside path
        // so the backend MangaResource.RootFolderPath matches the new Path.
        rootFolderPath,
      });
    }
  }, [
    manga,
    isPathChanging,
    isConfirmMoveModalOpen,
    pendingChanges,
    rootFolderPath,
    saveManga,
  ]);

  // "Yes, Move the Files" — saves with moveFiles=true. useSaveManga's
  // closure already has isPathChanging=true here (we only got here via
  // the isPathChanging confirm-modal gate), so the PUT carries
  // ?moveFiles=true automatically.
  const handleMoveMangaPress = useCallback(() => {
    setIsConfirmMoveModalOpen(false);

    saveManga({
      ...manga,
      ...pendingChanges,
      rootFolderPath,
    });
  }, [manga, pendingChanges, rootFolderPath, saveManga]);

  // Auto-close on successful save (mirrors EditSeriesModalContent's effect).
  useEffect(() => {
    if (!isSaving && wasSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, wasSaving, saveError, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('EditMangaModalHeader', { title: manga.title })}
      </ModalHeader>

      <ModalBody>
        <div data-testid="edit-manga-modal">
          <Form
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            <FormGroup size={sizes.MEDIUM}>
              <FormLabel>{translate('Monitored')}</FormLabel>

              <FormInputGroup
                type={inputTypes.CHECK}
                name="monitored"
                helpText={translate('MonitoredChaptersHelpText')}
                {...monitored}
                onChange={handleInputChange}
              />
            </FormGroup>

            <FormGroup size={sizes.MEDIUM}>
              <FormLabel>{translate('TranslationProfile')}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                name="translationProfileId"
                values={translationProfileValues}
                {...translationProfileId}
                onChange={handleInputChange}
              />
            </FormGroup>

            <FormGroup size={sizes.MEDIUM}>
              <FormLabel>{translate('CustomFormatProfile')}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                name="customFormatProfileId"
                values={customFormatProfileValues}
                {...customFormatProfileId}
                onChange={handleInputChange}
              />
            </FormGroup>

            <FormGroup size={sizes.MEDIUM}>
              <FormLabel>{translate('Path')}</FormLabel>

              {/* issue #81: replaced the prior "Path edit deferred" read-only
                <div> with an editable PATH FormInputGroup + RootFolder
                picker button. Clicking the button opens RootFolderModal;
                picking a destination updates path + rootFolderPath; the
                Save handler then routes through MoveMangaModal if the
                path actually changed. */}
              <FormInputGroup
                type={inputTypes.PATH}
                name="path"
                {...path}
                buttons={[
                  <FormInputButton
                    key="fileBrowser"
                    kind={kinds.DEFAULT}
                    title={translate('RootFolder')}
                    onPress={handleRootFolderPress}
                  >
                    <Icon name={icons.ROOT_FOLDER} />
                  </FormInputButton>,
                ]}
                includeFiles={false}
                onChange={handleInputChange}
              />
            </FormGroup>

            <FormGroup size={sizes.MEDIUM}>
              <FormLabel>{translate('Tags')}</FormLabel>

              {/* Phase 22 Plan 22-06 Task 4 — stable testid container for the
                  Tags select. Wraps the FormInputGroup so the Playwright
                  Page.GetByTestId('edit-manga-modal-tags-select') locator
                  resolves to the entire tag input region (input + dropdown
                  options render under this container). Follows the v1.1
                  data-testid-spec.md §"v1.1 Tag-Vertical Selectors" naming
                  convention `edit-manga-modal-tags-{element}`. */}
              <div data-testid="edit-manga-modal-tags-select">
                <FormInputGroup
                  type={inputTypes.TAG}
                  name="tags"
                  {...tags}
                  onChange={handleInputChange}
                />
              </div>
            </FormGroup>

            {/* quick-260618-eqz — user-owned alternative titles. Free-text
                TEXT_TAG chip input (comma / Enter to add, chip to remove).
                Rides the existing {...manga, ...pendingChanges} save spread,
                so no save-handler change is needed. */}
            <FormGroup size={sizes.MEDIUM}>
              <FormLabel>{translate('UserAlternativeTitles')}</FormLabel>

              <FormInputGroup
                type={inputTypes.TEXT_TAG}
                name="userAlternativeTitles"
                {...userAlternativeTitles}
                helpText={translate('UserAlternativeTitlesHelpText')}
                onChange={handleInputChange}
              />
            </FormGroup>
          </Form>
        </div>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div className={styles.modalFooterButtons}>
          <Button
            data-testid="edit-manga-modal-cancel-button"
            onPress={onModalClose}
          >
            {translate('Cancel')}
          </Button>

          <SpinnerErrorButton
            error={saveError}
            isSpinning={isSaving}
            data-testid="edit-manga-modal-save-button"
            onPress={handleSavePress}
          >
            {translate('Save')}
          </SpinnerErrorButton>
        </div>
      </ModalFooter>

      <RootFolderModal
        isOpen={isRootFolderModalOpen}
        mangaId={manga.id}
        rootFolderPath={rootFolderPath}
        onSavePress={handleRootFolderChange}
        onModalClose={handleRootFolderModalClose}
      />

      <MoveMangaModal
        originalPath={manga.path}
        destinationPath={pendingChanges.path}
        isOpen={isConfirmMoveModalOpen}
        onModalClose={handleCancelPress}
        onSavePress={handleSavePress}
        onMoveMangaPress={handleMoveMangaPress}
      />
    </ModalContent>
  );
}

function EditMangaModalContent({
  mangaId,
  onModalClose,
}: EditMangaModalContentProps) {
  const manga = useSingleManga(mangaId);

  if (!manga) {
    return null;
  }

  return <EditMangaForm manga={manga} onModalClose={onModalClose} />;
}

export default EditMangaModalContent;
