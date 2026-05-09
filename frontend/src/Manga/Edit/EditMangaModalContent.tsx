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
// === Editable field set (Issue #28 — 2026-05-09) ===
// All five fields below round-trip end-to-end through MangaResource +
// Manga.ApplyChanges. PR #27 originally shipped just Monitored + Tags because
// MonitorNewItems / TranslationProfileId / CustomFormatProfileId were not yet
// on the wire. Issue #28 closed those backend gaps; this modal now exposes
// the full editable surface that the bulk-edit modal also exposes (minus the
// NoChange semantics that only multi-select needs).
//
// Path / RootFolder remains deferred — RootFolderModal + MoveSeriesModal
// have not been re-shipped post-Plan-15-07.
//
// Phase 8 cleanup: this file IS the manga canonical now (Tv/ subtree gone).
import React, { useCallback, useEffect, useMemo } from 'react';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
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
import { inputTypes, sizes } from 'Helpers/Props';
import Manga, { MonitorNewItems } from 'Manga/Manga';
import { useSaveManga, useSingleManga } from 'Manga/useManga';
import selectSettings from 'Store/Selectors/selectSettings';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './EditMangaModalContent.css';

export interface EditMangaModalContentProps {
  mangaId: number;
  onModalClose: () => void;
}

// Editable subset of the Manga record. Fields here MUST round-trip through
// MangaResource + Manga.ApplyChanges; verify on the backend before extending.
interface EditableMangaFields {
  monitored: boolean;
  monitorNewItems: MonitorNewItems;
  translationProfileId: number;
  customFormatProfileId: number;
  tags: number[];
}

// 2-value MangaMonitorNewItems mirror — keys match the C# enum value names so
// JSON round-trip lands on the right enum value at the controller layer.
// Sonarr precedent: MonitorNewItemsSelectInput.tsx (which reads from a stub
// monitorNewItemsOptions array). For the single-manga modal we render a plain
// SELECT input bound to this 2-entry list — no NoChange / Mixed extras
// (those only matter for the bulk-edit case).
const monitorNewItemsValues: EnhancedSelectInputValue<string>[] = [
  {
    key: 'all',
    get value() {
      return translate('MonitorAllChapters');
    },
  },
  {
    key: 'none',
    get value() {
      return translate('MonitorNone');
    },
  },
];

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

  // Path-change support is deferred (see header note); always pass false to
  // useSaveManga so the backend skips the move-files branch.
  const { saveManga, isSaving, saveError } = useSaveManga(manga.id, false);
  const wasSaving = usePrevious(isSaving);

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
      // Default to 'all' for legacy rows where the backend default landed
      // before MonitorNewItems was on the wire.
      monitorNewItems: manga.monitorNewItems ?? 'all',
      // 0 means "fall back to Config.DefaultTranslationProfileId / Config.
      // DefaultCustomFormatProfileId" (Phase 5 D-11 default-seeded entities).
      translationProfileId: manga.translationProfileId ?? 0,
      customFormatProfileId: manga.customFormatProfileId ?? 0,
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

  const {
    monitored,
    monitorNewItems,
    translationProfileId,
    customFormatProfileId,
    tags,
  } = settings;

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
            <FormLabel>{translate('MonitorNewItems')}</FormLabel>

            <FormInputGroup
              type={inputTypes.SELECT}
              name="monitorNewItems"
              values={monitorNewItemsValues}
              helpText={translate('MonitorNewItemsHelpText')}
              {...monitorNewItems}
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
