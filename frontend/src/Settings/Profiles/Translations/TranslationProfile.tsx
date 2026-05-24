// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/Profiles/Quality/QualityProfile.tsx (lines 1-167).
//
// UI-07: Settings → Translation Profiles single-card display (Phase 5 D-01..D-04 entity).
//
// Manga sibling preserves: Card overlay onPress -> open-edit pattern; ConfirmModal delete-confirm flow;
// EditModal mounted-by-card-state pattern.
//
// Manga sibling diverges from QualityProfile:
//   * Renders an ordered language list (flat BCP-47 string[]; array index = preference rank, Phase 5 D-03)
//     instead of quality items + cutoff highlight
//   * `allowLanguagesNotInProfile` (Phase 5 D-02 strict-mode bool) replaces the cutoff logic
//   * `upgradeAllowed` (Phase 6 D-10 per-profile upgrade flag) is the manga peer of QualityProfile's
//     upgrade-allowed bool (GH #138 — surfaced in the edit modal, not on the card)
//   * No clone button in v1 (TranslationProfiles are simple enough; deferred)
//   * Delete-confirm uses UI-SPEC §Destructive confirmation copy: 'Delete the Translation Profile "{Name}"?' + 'Delete Profile'
//
// GH #127 (debug gh127-tprofile-langs-mismatch, 2026-05-14): the TS type below was
// built speculatively in Phase 7 D-05 against a `{language,rank,allowed}[]` + `isDefault`
// + `fallback` shape the Phase 5 backend never implemented. The shipped
// `/api/v5/translationprofile` resource serializes ONLY
// `{ id, name, languages: string[], allowLanguagesNotInProfile: bool }`. Fixed
// Frontend→backend (user-decided): the type + card now match the real API.
//
// GH #138 (debug gh138-tprofile-upgradeallowed, 2026-05-14): the entity carries an
// `UpgradeAllowed` bool (Phase 6 D-10, default true) that `UpgradeSpecification.cs`
// reads as the OUTER half of the D-10 three-state effective-upgrade-allowed AND-merge.
// The V5 resource originally dropped it, freezing it at `true`. The resource now
// round-trips `upgradeAllowed` and the edit modal exposes it as a checkbox; the type
// below carries the field.
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

// Matches the shipped backend TranslationProfileResource
// (src/Mangarr.Api.V5/Profiles/Translations/TranslationProfileResource.cs):
//   - `languages` is a flat ordered BCP-47 string[]; array index = preference rank (Phase 5 D-03).
//   - `allowLanguagesNotInProfile` is the Phase 5 D-02 strict-mode bool (default false).
//   - `upgradeAllowed` is the Phase 6 D-10 per-profile upgrade flag (default true) — the
//     OUTER half of UpgradeSpecification's D-10 three-state AND-merge (GH #138).
// The backend has NO per-profile `isDefault` flag — the default profile is the
// global `Config.DefaultTranslationProfileId` config key — and NO `fallback` enum
// or per-language `allowed`/`rank` fields. Those are not surfaced here.
export interface TranslationProfileResource {
  id: number;
  name: string;
  languages: string[];
  allowLanguagesNotInProfile: boolean;
  upgradeAllowed: boolean;
}

interface TranslationProfileProps extends TranslationProfileResource {
  onEditPress: (id: number) => void;
}

const PATH = '/translationprofile';

function TranslationProfile(props: TranslationProfileProps) {
  const { id, name, languages, onEditPress } = props;

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

  // `languages` is already in preference-rank order (array index = rank); render as-is.
  const orderedLanguages = languages ?? [];

  // Phase 30 Plan 30-02 fix-forward: data-testid moves from the inner content
  // <div> to the Card prop so it lands on the Card-underlay <Link> (the actual
  // interactive <button>). Card.tsx:12-17 documents this contract — placing the
  // testid on a child div produces the "Card-underlay intercepts pointer events"
  // Playwright failure mode (see .planning/debug/live-indexer-card-click.md).
  // SettingsFlow.SetTranslationProfileOrderAsync's row.ClickAsync() now lands
  // directly on the underlay button → onPress fires → EditModal opens.
  return (
    <Card
      className={styles.translationProfile}
      overlayContent={true}
      onPress={handleEditPress}
      data-testid={`settings-translation-profiles-row-${id}`}
    >
      <div>
        <div className={styles.nameContainer}>
          <div
            className={styles.name}
            data-testid={`settings-translation-profiles-row-${id}-name`}
          >
            {name}
          </div>
        </div>

        <div className={styles.languages}>
          {orderedLanguages.map((language) => {
            return (
              <Label key={language} kind={kinds.DEFAULT}>
                {language}
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
          aria-hidden="true"
          tabIndex={-1}
          style={{ display: 'none' }}
          onClick={handleDeletePress}
        />
      </div>
    </Card>
  );
}

export default TranslationProfile;
