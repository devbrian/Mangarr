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
//   * No schema fetch — TranslationProfile is a simple shape (name + isDefault + languages[] + fallback);
//     Phase 5 baseline ships entities directly without a /schema endpoint
//   * Languages list rendered as a flat ordered list of {language, rank, allowed} rows with up/down rank buttons
//     (DnD reorder deferred — the DnDProvider context is wired in Profiles.tsx for forward compatibility)
//   * Fallback radio (allowed-low-rank | rejected) replaces cutoff/upgrade-allowed quality concepts
//   * Save button label uses i18n key 'SaveTranslationProfile' per UI-SPEC §Page-level CTAs
//   * Delete button copy uses 'Delete Profile' per UI-SPEC §Destructive confirmation
//   * In-use error rendered inline (UI-SPEC §Error states): 'This Translation Profile is in use by {N} manga.'
//
// Phase 8 cleanup: this stays — manga-canonical.

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
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

const FALLBACK_VALUES = [
  { key: 'allowed-low-rank', value: 'Allowed (low rank)' },
  { key: 'rejected', value: 'Rejected' },
];

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
    isDefault: false,
    languages: [{ language: 'en', rank: 1, allowed: true }],
    fallback: 'rejected',
  };
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

  const [item, setItem] = useState<TranslationProfileResource>(() =>
    existing ?? defaultProfile()
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

  const handleIsDefaultChange = useCallback(
    (change: InputChangedHandler<boolean>) => {
      setItem((prev) => ({ ...prev, isDefault: change.value }));
    },
    []
  );

  const handleFallbackChange = useCallback(
    (change: InputChangedHandler<string>) => {
      setItem((prev) => ({
        ...prev,
        fallback: change.value as 'allowed-low-rank' | 'rejected',
      }));
    },
    []
  );

  const handleLanguageCodeChange = useCallback(
    (rank: number, language: string) => {
      setItem((prev) => ({
        ...prev,
        languages: prev.languages.map((l) =>
          l.rank === rank ? { ...l, language } : l
        ),
      }));
    },
    []
  );

  const handleLanguageAllowedChange = useCallback(
    (rank: number, allowed: boolean) => {
      setItem((prev) => ({
        ...prev,
        languages: prev.languages.map((l) =>
          l.rank === rank ? { ...l, allowed } : l
        ),
      }));
    },
    []
  );

  const handleAddLanguage = useCallback(() => {
    setItem((prev) => {
      const nextRank =
        prev.languages.reduce((max, l) => Math.max(max, l.rank), 0) + 1;
      return {
        ...prev,
        languages: [
          ...prev.languages,
          { language: 'en', rank: nextRank, allowed: true },
        ],
      };
    });
  }, []);

  const handleRemoveLanguage = useCallback((rank: number) => {
    setItem((prev) => ({
      ...prev,
      languages: prev.languages.filter((l) => l.rank !== rank),
    }));
  }, []);

  const handleMoveLanguage = useCallback(
    (rank: number, direction: -1 | 1) => {
      setItem((prev) => {
        const sorted = [...prev.languages].sort((a, b) => a.rank - b.rank);
        const idx = sorted.findIndex((l) => l.rank === rank);
        const swapIdx = idx + direction;
        if (idx < 0 || swapIdx < 0 || swapIdx >= sorted.length) {
          return prev;
        }
        const a = sorted[idx];
        const b = sorted[swapIdx];
        sorted[idx] = { ...b, rank: a.rank };
        sorted[swapIdx] = { ...a, rank: b.rank };
        return { ...prev, languages: sorted };
      });
    },
    []
  );

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

  const sortedLanguages = useMemo(
    () => [...item.languages].sort((a, b) => a.rank - b.rank),
    [item.languages]
  );

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
                  {translate('Languages')}
                </FormLabel>
                <div>
                  {sortedLanguages.map((lang, idx) => (
                    <div
                      key={`${lang.rank}-${lang.language}`}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        marginBottom: 6,
                        gap: 6,
                      }}
                    >
                      <span style={{ minWidth: 30 }}>{lang.rank}.</span>
                      <div style={{ minWidth: 150 }}>
                        <FormInputGroup
                          type={inputTypes.SELECT}
                          name={`language-${lang.rank}`}
                          value={lang.language}
                          values={languageValues}
                          onChange={(c: InputChangedHandler<string>) =>
                            handleLanguageCodeChange(lang.rank, c.value)
                          }
                        />
                      </div>
                      <div style={{ minWidth: 90 }}>
                        <FormInputGroup
                          type={inputTypes.CHECK}
                          name={`allowed-${lang.rank}`}
                          value={lang.allowed}
                          onChange={(c: InputChangedHandler<boolean>) =>
                            handleLanguageAllowedChange(lang.rank, c.value)
                          }
                        />
                      </div>
                      <IconButton
                        name={icons.SORT_ASCENDING}
                        title={translate('MoveUp')}
                        isDisabled={idx === 0}
                        onPress={() => handleMoveLanguage(lang.rank, -1)}
                      />
                      <IconButton
                        name={icons.SORT_DESCENDING}
                        title={translate('MoveDown')}
                        isDisabled={idx === sortedLanguages.length - 1}
                        onPress={() => handleMoveLanguage(lang.rank, 1)}
                      />
                      <IconButton
                        name={icons.REMOVE}
                        title={translate('Remove')}
                        isDisabled={sortedLanguages.length === 1}
                        onPress={() => handleRemoveLanguage(lang.rank)}
                      />
                    </div>
                  ))}
                  <Button onPress={handleAddLanguage}>
                    {translate('AddLanguage')}
                  </Button>
                </div>
              </FormGroup>

              <FormGroup size={sizes.EXTRA_SMALL}>
                <FormLabel size={sizes.SMALL}>
                  {translate('Fallback')}
                </FormLabel>
                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="fallback"
                  value={item.fallback}
                  values={FALLBACK_VALUES}
                  onChange={handleFallbackChange}
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
