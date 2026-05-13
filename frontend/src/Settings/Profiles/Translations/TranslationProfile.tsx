// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/Profiles/Quality/QualityProfile.tsx (lines 1-167).
//
// UI-07: Settings → Translation Profiles single-card display (Phase 5 D-01..D-04 entity).
//
// Manga sibling preserves: Card overlay onPress -> open-edit pattern; ConfirmModal delete-confirm flow;
// EditModal mounted-by-card-state pattern.
//
// Manga sibling diverges from QualityProfile:
//   * Renders ranked language list (BCP-47 codes + allowed flag) instead of quality items + cutoff highlight
//   * isDefault badge replaces the cutoff/upgrade-allowed logic
//   * No clone button in v1 (TranslationProfiles are simple enough; deferred)
//   * Delete-confirm uses UI-SPEC §Destructive confirmation copy: 'Delete the Translation Profile "{Name}"?' + 'Delete Profile'
//
// Phase 8 cleanup: this stays — manga-canonical.

import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useState } from 'react';
import Card from 'Components/Card';
import Label from 'Components/Label';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './TranslationProfile.css';

export interface TranslationProfileLanguage {
  language: string;
  rank: number;
  allowed: boolean;
}

export interface TranslationProfileResource {
  id: number;
  name: string;
  isDefault: boolean;
  languages: TranslationProfileLanguage[];
  fallback: 'allowed-low-rank' | 'rejected';
}

interface TranslationProfileProps extends TranslationProfileResource {
  onEditPress: (id: number) => void;
}

const PATH = '/translationprofile';

function TranslationProfile(props: TranslationProfileProps) {
  const { id, name, isDefault, languages, onEditPress } = props;

  const queryClient = useQueryClient();
  const { mutate: deleteProfile, isPending: isDeleting } = useApiMutation<
    void,
    void
  >({
    path: `${PATH}/${id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
      },
    },
  });

  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);

  const handleEditPress = useCallback(() => {
    onEditPress(id);
  }, [id, onEditPress]);

  const handleDeletePress = useCallback(() => {
    setIsDeleteModalOpen(true);
  }, []);

  const handleDeleteModalClose = useCallback(() => {
    setIsDeleteModalOpen(false);
  }, []);

  const handleConfirmDelete = useCallback(() => {
    deleteProfile();
    setIsDeleteModalOpen(false);
  }, [deleteProfile]);

  // Sort by rank ascending; only show allowed languages on the card chips (mirrors Quality cutoff highlight).
  const allowedLanguages = languages
    ? [...languages].filter((l) => l.allowed).sort((a, b) => a.rank - b.rank)
    : [];

  // Phase 18 Plan 18-07: per-row testid for SettingsFlow.SetTranslationProfileOrderAsync
  // (D-08 catalog). The inner wrapper <div data-testid="settings-translation-profiles-
  // row-{id}"> guarantees the testid lands on a real DOM node regardless of Card's
  // prop-spread behavior.
  return (
    <Card
      className={styles.translationProfile}
      overlayContent={true}
      onPress={handleEditPress}
    >
      <div data-testid={`settings-translation-profiles-row-${id}`}>
        <div className={styles.nameContainer}>
          <div
            className={styles.name}
            data-testid={`settings-translation-profiles-row-${id}-name`}
          >
            {name}
          </div>

          {isDefault ? (
            <Label kind={kinds.INFO}>{translate('Default')}</Label>
          ) : null}
        </div>

        <div className={styles.languages}>
          {allowedLanguages.map((lang) => {
            return (
              <Label key={lang.language} kind={kinds.DEFAULT}>
                {lang.language}
              </Label>
            );
          })}
        </div>

        <ConfirmModal
          isOpen={isDeleteModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteTranslationProfile')}
          message={translate('DeleteTranslationProfileMessageText', { name })}
          confirmLabel={translate('DeleteProfile')}
          isSpinning={isDeleting}
          onConfirm={handleConfirmDelete}
          onCancel={handleDeleteModalClose}
        />

        {/* Hidden control hook so the parent EditTranslationProfileModal can reach the delete confirmation
            via onDeleteTranslationProfilePress prop. The modal renders its own delete button; this card
            surfaces the same flow if a future Phase 8 design adds a card-level delete icon. */}
        <button
          type="button"
          className={styles.hiddenDeleteHook}
          onClick={handleDeletePress}
          aria-hidden="true"
          tabIndex={-1}
          style={{ display: 'none' }}
        />
      </div>
    </Card>
  );
}

export default TranslationProfile;
