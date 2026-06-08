// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/Profiles/Quality/EditQualityProfileModalContent.tsx (lines 1-749).
//
// UI-07: Edit modal CONTENT for the TranslationProfile editor (Phase 5 Plan 05-02 entity).
//
// Manga sibling preserves: Form / FormGroup / FormInputGroup scaffold; SpinnerErrorButton save flow;
// ModalContent + ModalHeader + ModalBody + ModalFooter layout; auto-height measure pattern; Cancel + Save +
// (id ? Delete) button row.
//
// Manga sibling diverges from EditQualityProfileModalContent:
//   * No schema fetch — TranslationProfile is a simple shape (name + languages[] +
//     allowLanguagesNotInProfile + upgradeAllowed); Phase 5 baseline ships entities directly
//     without a /schema endpoint
//   * Languages list rendered as a flat ordered list of BCP-47 string rows with up/down move buttons;
//     the array index IS the preference rank (Phase 5 D-03), so the rendered "1." / "2." labels are
//     1-based array positions and reorder/remove just splices the array
//   * `allowLanguagesNotInProfile` checkbox (Phase 5 D-02 strict-mode bool) replaces the
//     cutoff quality concept
//   * `upgradeAllowed` checkbox (Phase 6 D-10 per-profile upgrade flag, default true) is the manga
//     peer of QualityProfile's upgrade-allowed bool (GH #138)
//   * Save button label uses i18n key 'SaveTranslationProfile' per UI-SPEC §Page-level CTAs
//   * Delete button copy uses 'Delete Profile' per UI-SPEC §Destructive confirmation
//   * In-use error rendered inline (UI-SPEC §Error states): 'This Translation Profile is in use by {N} manga.'
//
// GH #127 (debug gh127-tprofile-langs-mismatch, 2026-05-14): this modal was built in
// Phase 7 D-05 against a speculative `{language,rank,allowed}[]` + `isDefault` + `fallback`
// shape the Phase 5 backend never implemented — `GET /api/v5/translationprofile` returns
// `languages` as a flat `string[]`, so `lang.rank` / `lang.allowed` were `undefined` and
// `handleAddLanguage` computed `NaN`. Fixed Frontend→backend (user-decided): the modal now
// treats `languages` as `string[]`, derives rank from the array index, and replaces the
// phantom `isDefault` checkbox + `fallback` select with the real `allowLanguagesNotInProfile`
// checkbox. The backend is untouched.
//
// GH #138 (debug gh138-tprofile-upgradeallowed, 2026-05-14): the entity carries an
// `UpgradeAllowed` bool (Phase 6 D-10, default true) that `UpgradeSpecification.cs` reads
// as the OUTER half of the D-10 three-state effective-upgrade-allowed AND-merge. The V5
// resource originally dropped it, freezing it at `true`. The resource now round-trips
// `upgradeAllowed`; this modal exposes it as a checkbox below the strict-mode checkbox.
//
// Phase 8 cleanup: this stays — manga-canonical.

import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import useMeasure from 'Helpers/Hooks/useMeasure';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { icons, inputTypes, kinds, sizes } from 'Helpers/Props';
import useManga from 'Manga/useManga';
import translate from 'Utilities/String/translate';
import { TranslationProfileResource } from './TranslationProfile';

const PATH = '/translationprofile';

// Common BCP-47 codes for manga translations (extensible; user can type any value).
const COMMON_LANGUAGE_CODES = [
  'en',
  'ja',
  'zh',
  'ko',
  'es',
  'fr',
  'de',
  'pt',
  'ru',
  'it',
  'id',
  'vi',
  'th',
  'ar',
];

interface InputChangedHandler<T> {
  name: string;
  value: T;
}

interface EditTranslationProfileModalContentProps {
  id?: number;
  onContentHeightChange: (height: number) => void;
  onModalClose: () => void;
}

function defaultProfile(): TranslationProfileResource {
  return {
    id: 0,
    name: '',
    languages: ['en'],
    allowLanguagesNotInProfile: false,
    // Phase 6 D-10: per-profile upgrade flag defaults TRUE (language rank ordered, *arr promise).
    upgradeAllowed: true,
    // quick-260608-gmm: wired to Config.DefaultTranslationProfileId (backend computes on read,
    // sets the global key on save when true). A brand-new profile is not default until checked.
    isDefault: false,
  };
}

// Per-row sub-component for the ordered language list. Extracted so the parent's
// .map() does not allocate fresh arrow handlers per render (react/jsx-no-bind);
// each row binds its own array index into stable useCallback handlers here.
//
// GH #127: the backend `languages` payload is a flat `string[]`, so a row has no
// stable identity of its own — its preference rank IS its array index. Handlers
// therefore key off `idx` (not a phantom `lang.rank`), and the rendered "{idx + 1}."
// label is the 1-based rank.
interface LanguageRankRowProps {
  language: string;
  idx: number;
  isLast: boolean;
  isOnly: boolean;
  languageValues: { key: string; value: string }[];
  onCodeChange: (idx: number, language: string) => void;
  onMove: (idx: number, direction: -1 | 1) => void;
  onRemove: (idx: number) => void;
}

function LanguageRankRow({
  language,
  idx,
  isLast,
  isOnly,
  languageValues,
  onCodeChange,
  onMove,
  onRemove,
}: LanguageRankRowProps) {
  const handleCodeChange = useCallback(
    (c: InputChangedHandler<string>) => {
      onCodeChange(idx, c.value);
    },
    [idx, onCodeChange]
  );

  const handleMoveUp = useCallback(() => {
    onMove(idx, -1);
  }, [idx, onMove]);

  const handleMoveDown = useCallback(() => {
    onMove(idx, 1);
  }, [idx, onMove]);

  const handleRemove = useCallback(() => {
    onRemove(idx);
  }, [idx, onRemove]);

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        marginBottom: 6,
        gap: 6,
      }}
    >
      <span style={{ minWidth: 30 }}>{idx + 1}.</span>
      <div style={{ minWidth: 150 }}>
        <FormInputGroup
          type={inputTypes.SELECT}
          name={`language-${idx}`}
          value={language}
          values={languageValues}
          onChange={handleCodeChange}
        />
      </div>
      {/* Phase 30 Plan 30-02 (II2-07) — testid for SettingsFlow.SetTranslationProfileOrderAsync.
          The IconButton wrapper forwards data-testid via Link's ...otherProps (per
          frontend/src/Components/Link/Link.tsx — Phase 18 wrapper-sweep contract). */}
      <IconButton
        data-testid={`settings-translation-profiles-edit-row-${idx}-up`}
        name={icons.SORT_ASCENDING}
        title={translate('MoveUp')}
        isDisabled={idx === 0}
        onPress={handleMoveUp}
      />
      <IconButton
        data-testid={`settings-translation-profiles-edit-row-${idx}-down`}
        name={icons.SORT_DESCENDING}
        title={translate('MoveDown')}
        isDisabled={isLast}
        onPress={handleMoveDown}
      />
      <IconButton
        name={icons.REMOVE}
        title={translate('Remove')}
        isDisabled={isOnly}
        onPress={handleRemove}
      />
    </div>
  );
}

function EditTranslationProfileModalContent({
  id,
  onContentHeightChange,
  onModalClose,
}: EditTranslationProfileModalContentProps) {
  const queryClient = useQueryClient();

  // Load the list to find the existing profile when editing.
  const { data: profiles, isFetching: isListFetching } = useApiQuery<
    TranslationProfileResource[]
  >({
    path: PATH,
    queryOptions: {
      gcTime: Infinity,
      staleTime: 5 * 60 * 1000,
    },
  });

  const existing = useMemo(() => {
    if (id === undefined || id === 0 || !profiles) {
      return undefined;
    }
    return profiles.find((p) => p.id === id);
  }, [id, profiles]);

  const [item, setItem] = useState<TranslationProfileResource>(
    () => existing ?? defaultProfile()
  );

  // Re-seed when the existing profile resolves after an initial undefined.
  useEffect(() => {
    if (existing) {
      setItem(existing);
    }
  }, [existing]);

  // In-use count (Manga referencing this profile).
  const { data: mangaList = [] } = useManga();
  const inUseCount = useMemo(() => {
    if (id === undefined || id === 0) {
      return 0;
    }
    return mangaList.filter(
      (m: { translationProfileId?: number }) => m.translationProfileId === id
    ).length;
  }, [id, mangaList]);
  const isInUse = inUseCount > 0;

  // Save mutation
  const isEditing = id !== undefined && id !== 0;
  const {
    mutate: save,
    isPending: isSaving,
    error: saveError,
  } = useApiMutation<TranslationProfileResource, TranslationProfileResource>({
    path: isEditing ? `${PATH}/${id}` : PATH,
    method: isEditing ? 'PUT' : 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
      },
    },
  });

  // Delete mutation
  const {
    mutate: deleteProfile,
    isPending: isDeleting,
    error: deleteError,
  } = useApiMutation<void, void>({
    path: `${PATH}/${id ?? 0}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
      },
    },
  });

  const [isDeleteConfirmOpen, setIsDeleteConfirmOpen] = useState(false);

  const wasSaving = usePrevious(isSaving);
  const wasDeleting = usePrevious(isDeleting);

  // Auto-close on save success
  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, wasSaving, saveError, onModalClose]);

  // Auto-close on delete success
  useEffect(() => {
    if (wasDeleting && !isDeleting && !deleteError) {
      onModalClose();
    }
  }, [isDeleting, wasDeleting, deleteError, onModalClose]);

  // Height measurement
  const [measureHeaderRef, { height: headerHeight }] = useMeasure();
  const [measureBodyRef, { height: bodyHeight }] = useMeasure();
  const [measureFooterRef, { height: footerHeight }] = useMeasure();

  useEffect(() => {
    onContentHeightChange(headerHeight + bodyHeight + footerHeight + 40);
  }, [headerHeight, bodyHeight, footerHeight, onContentHeightChange]);

  // Field handlers
  const handleNameChange = useCallback(
    (change: InputChangedHandler<string>) => {
      setItem((prev) => ({ ...prev, name: change.value }));
    },
    []
  );

  const handleAllowLanguagesNotInProfileChange = useCallback(
    (change: InputChangedHandler<boolean>) => {
      setItem((prev) => ({
        ...prev,
        allowLanguagesNotInProfile: change.value,
      }));
    },
    []
  );

  // GH #138: per-profile upgrade gate (Phase 6 D-10). Round-trips through
  // TranslationProfileResource.UpgradeAllowed and is read by UpgradeSpecification.cs.
  const handleUpgradeAllowedChange = useCallback(
    (change: InputChangedHandler<boolean>) => {
      setItem((prev) => ({
        ...prev,
        upgradeAllowed: change.value,
      }));
    },
    []
  );

  // quick-260608-gmm: checking Default points Config.DefaultTranslationProfileId at this
  // profile on save (the controller moves the single global default). Unchecking is a backend
  // no-op — the default is changed by checking a different profile.
  const handleIsDefaultChange = useCallback(
    (change: InputChangedHandler<boolean>) => {
      setItem((prev) => ({
        ...prev,
        isDefault: change.value,
      }));
    },
    []
  );

  // `languages` is a flat string[]; rank = array index, so all mutations operate
  // on the index directly. Editing a code replaces the entry at `idx`.
  const handleLanguageCodeChange = useCallback(
    (idx: number, language: string) => {
      setItem((prev) => ({
        ...prev,
        languages: prev.languages.map((l, i) => (i === idx ? language : l)),
      }));
    },
    []
  );

  const handleAddLanguage = useCallback(() => {
    setItem((prev) => {
      // Default the new row to the first common code not already in the profile,
      // falling back to 'en' if every common code is already present.
      const nextCode =
        COMMON_LANGUAGE_CODES.find((c) => !prev.languages.includes(c)) ?? 'en';
      return {
        ...prev,
        languages: [...prev.languages, nextCode],
      };
    });
  }, []);

  const handleRemoveLanguage = useCallback((idx: number) => {
    setItem((prev) => ({
      ...prev,
      languages: prev.languages.filter((_, i) => i !== idx),
    }));
  }, []);

  const handleMoveLanguage = useCallback((idx: number, direction: -1 | 1) => {
    setItem((prev) => {
      const swapIdx = idx + direction;
      if (idx < 0 || swapIdx < 0 || swapIdx >= prev.languages.length) {
        return prev;
      }
      const languages = [...prev.languages];
      const [moved] = languages.splice(idx, 1);
      languages.splice(swapIdx, 0, moved);
      return { ...prev, languages };
    });
  }, []);

  const handleSavePress = useCallback(() => {
    save(item);
  }, [save, item]);

  const handleDeletePress = useCallback(() => {
    setIsDeleteConfirmOpen(true);
  }, []);

  const handleConfirmDelete = useCallback(() => {
    deleteProfile();
    setIsDeleteConfirmOpen(false);
  }, [deleteProfile]);

  const handleCancelDelete = useCallback(() => {
    setIsDeleteConfirmOpen(false);
  }, []);

  const languageValues = COMMON_LANGUAGE_CODES.map((c) => ({
    key: c,
    value: c,
  }));

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader ref={measureHeaderRef}>
        {isEditing
          ? translate('EditTranslationProfile')
          : translate('AddTranslationProfile')}
      </ModalHeader>

      <ModalBody>
        <div ref={measureBodyRef}>
          {isListFetching && isEditing && !existing ? (
            <LoadingIndicator />
          ) : (
            <Form>
              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>{translate('Name')}</FormLabel>
                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="name"
                  value={item.name}
                  onChange={handleNameChange}
                />
              </FormGroup>

              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>
                  {translate('Languages')}
                </FormLabel>
                <div>
                  {item.languages.map((language, idx) => (
                    <LanguageRankRow
                      key={`${idx}-${language}`}
                      language={language}
                      idx={idx}
                      isLast={idx === item.languages.length - 1}
                      isOnly={item.languages.length === 1}
                      languageValues={languageValues}
                      onCodeChange={handleLanguageCodeChange}
                      onMove={handleMoveLanguage}
                      onRemove={handleRemoveLanguage}
                    />
                  ))}
                  <Button onPress={handleAddLanguage}>
                    {translate('AddLanguage')}
                  </Button>
                </div>
              </FormGroup>

              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>
                  {translate('DefaultProfile')}
                </FormLabel>
                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="isDefault"
                  value={item.isDefault}
                  onChange={handleIsDefaultChange}
                />
              </FormGroup>

              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>
                  {translate('AllowLanguagesNotInProfile')}
                </FormLabel>
                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="allowLanguagesNotInProfile"
                  value={item.allowLanguagesNotInProfile}
                  helpText={translate('AllowLanguagesNotInProfileHelpText')}
                  onChange={handleAllowLanguagesNotInProfileChange}
                />
              </FormGroup>

              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>
                  {translate('UpgradeAllowed')}
                </FormLabel>
                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="upgradeAllowed"
                  value={item.upgradeAllowed}
                  helpText={translate('UpgradeAllowedHelpText')}
                  onChange={handleUpgradeAllowedChange}
                />
              </FormGroup>

              {isInUse && isEditing ? (
                <Alert kind={kinds.WARNING}>
                  {translate('TranslationProfileInUseHelpText', {
                    count: inUseCount,
                  })}
                </Alert>
              ) : null}
            </Form>
          )}
        </div>
      </ModalBody>

      <ModalFooter ref={measureFooterRef}>
        {isEditing ? (
          <Button
            kind={kinds.DANGER}
            isDisabled={isInUse}
            onPress={handleDeletePress}
          >
            {translate('DeleteTranslationProfile')}
          </Button>
        ) : null}

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          data-testid="settings-translation-profiles-edit-save"
          isSpinning={isSaving}
          error={saveError}
          onPress={handleSavePress}
        >
          {translate('SaveTranslationProfile')}
        </SpinnerErrorButton>
      </ModalFooter>

      <ConfirmModal
        isOpen={isDeleteConfirmOpen}
        kind={kinds.DANGER}
        title={translate('DeleteTranslationProfile')}
        message={translate('DeleteTranslationProfileMessageText', {
          name: item.name,
        })}
        confirmLabel={translate('DeleteProfile')}
        isSpinning={isDeleting}
        onConfirm={handleConfirmDelete}
        onCancel={handleCancelDelete}
      />
    </ModalContent>
  );
}

export default EditTranslationProfileModalContent;
