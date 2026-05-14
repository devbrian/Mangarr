// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/Profiles/Quality/QualityProfiles.tsx (lines 1-79).
//
// UI-07: Settings → Translation Profiles list page (Phase 5 D-01..D-04 entity).
//
// Manga sibling preserves: list-page shape (FieldSet + PageSectionContent + map of profile cards + add-new card +
// EditModal wrapper); local-state add/edit/clone modal pattern.
//
// Manga sibling diverges from QualityProfiles:
//   * Wires /api/v5/translationprofile (Phase 5 Plan 05-02) instead of /api/v5/qualityprofile
//   * Renders TranslationProfile cards (ordered BCP-47 language list) instead of quality-items
//   * No clone-profile flow in v1 (deferred — TranslationProfiles are simple enough a fresh add is faster than cloning)
//
// Phase 8 cleanup: this stays — manga-canonical.

import React, { useCallback, useState } from 'react';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons } from 'Helpers/Props';
import sortByProp from 'Utilities/Array/sortByProp';
import translate from 'Utilities/String/translate';
import EditTranslationProfileModal from './EditTranslationProfileModal';
import TranslationProfile, {
  TranslationProfileResource,
} from './TranslationProfile';
import styles from './TranslationProfiles.css';

const PATH = '/translationprofile';

function TranslationProfiles() {
  const { data, error, isFetching, isFetched } = useApiQuery<
    TranslationProfileResource[]
  >({
    path: PATH,
    queryOptions: {
      gcTime: Infinity,
      staleTime: 5 * 60 * 1000,
    },
  });

  const sortedItems = data ? [...data].sort(sortByProp('name')) : [];

  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [editId, setEditId] = useState<number | undefined>(undefined);

  const handleAddPress = useCallback(() => {
    setEditId(undefined);
    setIsEditModalOpen(true);
  }, []);

  const handleModalClose = useCallback(() => {
    setEditId(undefined);
    setIsEditModalOpen(false);
  }, []);

  const handleEditPress = useCallback((id: number) => {
    setEditId(id);
    setIsEditModalOpen(true);
  }, []);

  return (
    <FieldSet legend={translate('TranslationProfiles')}>
      <PageSectionContent
        errorMessage={translate('TranslationProfilesLoadError')}
        error={error}
        isFetching={isFetching}
        isPopulated={isFetched}
      >
        <div className={styles.translationProfiles}>
          {sortedItems.map((item) => {
            return (
              <TranslationProfile
                key={item.id}
                {...item}
                onEditPress={handleEditPress}
              />
            );
          })}

          <Card
            className={styles.addTranslationProfile}
            onPress={handleAddPress}
          >
            <div className={styles.center}>
              <Icon name={icons.ADD} size={45} />
            </div>
          </Card>
        </div>

        <EditTranslationProfileModal
          id={editId}
          isOpen={isEditModalOpen}
          onModalClose={handleModalClose}
        />
      </PageSectionContent>
    </FieldSet>
  );
}

export default TranslationProfiles;
