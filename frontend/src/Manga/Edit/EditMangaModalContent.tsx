// Sonarr divergence: NEW manga sibling per Phase 15 Plan 15-12 deferred
// "v1.1+ dedicated single-manga Edit modal" — referenced in
// frontend/src/Manga/Details/MangaDetails.tsx (Edit button toolbar wiring).
// Role-match analog: frontend/src/Series/Edit/EditSeriesModalContent.tsx
// (deleted in Plan 15-07; canonical reference is git commit 0521a6c39).
//
// Manga sibling preserves: ModalContent layout, Form/FormGroup/FormInputGroup
// component shell, usePendingChangesStore + selectSettings plumbing,
// SpinnerErrorButton submit, MoveSeriesModal-equivalent confirmation flow
// (DEFERRED — see "Path edit deferred" note below).
// Manga sibling diverges from EditSeriesModalContent (per Phase 15 + design
// philosophy "Preserve Sonarr's shape wherever it works"):
//   * Path edit DEFERRED to a future release: RootFolderModal +
//     MoveSeriesModal were deleted in Plan 15-07 cascade and have not been
//     re-shipped. Path is rendered as a read-only label.
//   * No Redux clearPendingChanges dispatch on close — usePendingChangesStore
//     is now Zustand-local (a per-instance store), so unmounting the modal
//     discards pending changes automatically. This is the post-Phase-15
//     Mangarr convention; the old EditSeriesModal Redux wrapper is gone.
//   * No "Delete" button in the footer: the Delete toolbar button on
//     MangaDetails is its own modal (Plan 15-12 second deferred item) and
//     will be wired in a sibling fix-forward PR.
//
// === v1.1 minimum-viable scope (2026-05-08) ===
// The fields exposed below are intentionally a subset of the editable surface
// AddNewMangaModalContent ships, because the backend persistence path is not
// yet in place for the others:
//
//   * `Monitored`            — Manga.ApplyChanges line 116 copies it. ✓
//   * `Tags`                 — Manga.ApplyChanges line 115 copies it. ✓
//   * `MonitorNewItems`      — NOT on MangaResource and NOT in Manga.cs.
//                              Deferred until the resource + ApplyChanges land.
//   * `TranslationProfileId` — On Manga.cs line 37 but NOT exposed on
//                              MangaResource and NOT copied by ApplyChanges.
//                              Deferred until both extensions land.
//   * `CustomFormatProfileId`— Same as TranslationProfileId.
//   * `Path` / RootFolder    — Deferred (no RootFolderModal).
//
// Re-add fields as the backend MangaResource + Manga.ApplyChanges grow. The
// Sonarr-side EditSeriesModalContent (commit 0521a6c39) is the structural
// reference — drop in a FormGroup per field once the persistence side ships.
//
// Phase 8 cleanup: this file IS the manga canonical now (Tv/ subtree gone).
import React, { useCallback, useEffect, useMemo } from 'react';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import { usePendingChangesStore } from 'Helpers/Hooks/usePendingChangesStore';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { inputTypes, sizes } from 'Helpers/Props';
import Manga from 'Manga/Manga';
import { useSaveManga, useSingleManga } from 'Manga/useManga';
import selectSettings from 'Store/Selectors/selectSettings';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './EditMangaModalContent.css';

export interface EditMangaModalContentProps {
  mangaId: number;
  onModalClose: () => void;
}

// Editable subset of the Manga record. Only fields the backend actually
// persists today are listed here (see file header "v1.1 minimum-viable
// scope" note). Other Manga fields ride through unchanged in the PUT
// payload via spread of `manga` in handleSavePress.
interface EditableMangaFields {
  monitored: boolean;
  tags: number[];
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

  // Path-change support is deferred (see header note); always pass false to
  // useSaveManga so the backend skips the move-files branch.
  const { saveManga, isSaving, saveError } = useSaveManga(manga.id, false);
  const wasSaving = usePrevious(isSaving);

  const initial = useMemo<EditableMangaFields>(
    () => ({
      monitored: manga.monitored,
      tags: manga.tags,
    }),
    [manga]
  );

  const { settings, validationErrors, validationWarnings } = useMemo(() => {
    return {
      ...selectSettings(initial, pendingChanges, saveError),
      ...getValidationFailures(saveError),
    };
  }, [initial, pendingChanges, saveError]);

  const { monitored, tags } = settings;

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      // @ts-expect-error name is keyof EditableMangaFields at runtime
      setPendingChange(name, value);
    },
    [setPendingChange]
  );

  const handleSavePress = useCallback(() => {
    saveManga({ ...manga, ...pendingChanges });
  }, [manga, pendingChanges, saveManga]);

  // Auto-close on successful save (mirrors EditSeriesModalContent's effect).
  useEffect(() => {
    if (!isSaving && wasSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, wasSaving, saveError, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('EditSeriesModalHeader', { title: manga.title })}
      </ModalHeader>

      <ModalBody>
        <Form
          validationErrors={validationErrors}
          validationWarnings={validationWarnings}
        >
          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('Monitored')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="monitored"
              helpText={translate('MonitoredEpisodesHelpText')}
              {...monitored}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('Path')}</FormLabel>

            {/* Path edit deferred — see file header. Read-only label so the
                user can confirm the on-disk location. Root-folder change +
                file move come back in a future release. */}
            <div className={styles.pathReadOnly}>{manga.path}</div>
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('Tags')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TAG}
              name="tags"
              {...tags}
              onChange={handleInputChange}
            />
          </FormGroup>
        </Form>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div className={styles.modalFooterButtons}>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <SpinnerErrorButton
            error={saveError}
            isSpinning={isSaving}
            onPress={handleSavePress}
          >
            {translate('Save')}
          </SpinnerErrorButton>
        </div>
      </ModalFooter>
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
