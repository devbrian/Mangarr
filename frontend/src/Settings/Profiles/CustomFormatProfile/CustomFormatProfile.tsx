// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/Profiles/Quality/QualityProfile.tsx (lines 1-167).
//
// UI-07: Settings → Custom Format Profiles single-card display (Phase 5 Plan 05-03 entity).
//
// Manga sibling preserves: Card overlay onPress -> open-edit pattern; ConfirmModal delete-confirm flow.
//
// Manga sibling diverges from QualityProfile:
//   * Renders formatItems summary (count of allowed formats + min/max/default score range) instead of
//     quality-items + cutoff highlight
//   * isDefault badge replaces cutoff/upgrade-allowed quality concepts
//   * upgradeAllowed shown as a small badge label
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
import styles from './CustomFormatProfile.css';

export interface CustomFormatProfileFormatItem {
  // Wire field is `format` (the CustomFormat id) per CustomFormatProfileFormatItemResource
  // (Mangarr.Api.V5). It was previously typed `customFormatId`, which exists on no payload —
  // so the editor read undefined (blank format dropdown) and wrote `format` as 0 on save.
  format: number;
  score: number;
}

export interface CustomFormatProfileResource {
  id: number;
  name: string;
  isDefault: boolean;
  formatItems: CustomFormatProfileFormatItem[];
  minFormatScore: number;
  maxFormatScore: number;
  upgradeAllowed: boolean;
}

interface CustomFormatProfileProps extends CustomFormatProfileResource {
  onEditPress: (id: number) => void;
}

const PATH = '/customformatprofile';

function CustomFormatProfile(props: CustomFormatProfileProps) {
  const {
    id,
    name,
    isDefault,
    formatItems,
    minFormatScore,
    maxFormatScore,
    upgradeAllowed,
    onEditPress,
  } = props;

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

  const handleDeleteModalClose = useCallback(() => {
    setIsDeleteModalOpen(false);
  }, []);

  const handleConfirmDelete = useCallback(() => {
    deleteProfile();
    setIsDeleteModalOpen(false);
  }, [deleteProfile]);

  const allowedFormats = formatItems
    ? formatItems.filter((f) => f.score > 0).length
    : 0;

  return (
    <Card
      className={styles.customFormatProfile}
      overlayContent={true}
      onPress={handleEditPress}
    >
      <div className={styles.nameContainer}>
        <div className={styles.name}>{name}</div>

        {isDefault ? (
          <Label kind={kinds.INFO}>{translate('Default')}</Label>
        ) : null}
      </div>

      <div className={styles.scoreRange}>
        <Label kind={kinds.DEFAULT}>
          {translate('MinFormatScoreLabel', { score: minFormatScore })}
        </Label>
        <Label kind={kinds.DEFAULT}>
          {translate('MaxFormatScoreLabel', { score: maxFormatScore })}
        </Label>
        <Label kind={kinds.DEFAULT}>
          {translate('AllowedFormatsCount', { count: allowedFormats })}
        </Label>
        {upgradeAllowed ? (
          <Label kind={kinds.SUCCESS}>{translate('UpgradesAllowed')}</Label>
        ) : null}
      </div>

      <ConfirmModal
        isOpen={isDeleteModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteCustomFormatProfile')}
        message={translate('DeleteCustomFormatProfileMessageText', { name })}
        confirmLabel={translate('DeleteProfile')}
        isSpinning={isDeleting}
        onConfirm={handleConfirmDelete}
        onCancel={handleDeleteModalClose}
      />
    </Card>
  );
}

export default CustomFormatProfile;
