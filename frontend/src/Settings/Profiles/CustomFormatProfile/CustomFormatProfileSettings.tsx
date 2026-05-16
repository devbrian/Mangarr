// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/Profiles/Quality/QualityProfiles.tsx (lines 1-79) — list-page mirror,
// PLUS PageContent + PageContentBody wrap (top-level settings page route target).
//
// UI-07: Settings → Custom Format Profiles top-level page (Phase 5 Plan 05-03 entity).
//
// Manga sibling preserves: list-page shape (FieldSet + map of profile cards + add-new card + EditModal); also
// PageContent/PageContentBody scaffold for the top-level settings route (mirrors Quality.tsx).
//
// Manga sibling diverges from QualityProfiles:
//   * Wires /api/v5/customformatprofile (Phase 5 Plan 05-03) — separate entity from CustomFormat
//   * Renders CustomFormatProfile cards (formatItems + minFormatScore + maxFormatScore + upgradeAllowed)
//   * Top-level page (route /settings/customformatprofiles) — wraps PageContent + SettingsToolbar (Profiles.tsx
//     wraps the TranslationProfiles list inside its own DndProvider; this page does its own)
//
// Phase 8 cleanup: this stays — manga-canonical.

import { HTML5toTouch } from 'rdndmb-html5-to-touch';
import React, { useCallback, useState } from 'react';
import { DndProvider } from 'react-dnd-multi-backend';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageSectionContent from 'Components/Page/PageSectionContent';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons } from 'Helpers/Props';
import SettingsToolbar from 'Settings/SettingsToolbar';
import sortByProp from 'Utilities/Array/sortByProp';
import translate from 'Utilities/String/translate';
import CustomFormatProfile, {
  CustomFormatProfileResource,
} from './CustomFormatProfile';
import EditCustomFormatProfileModal from './EditCustomFormatProfileModal';
import styles from './CustomFormatProfileSettings.css';

const PATH = '/customformatprofile';

function CustomFormatProfileSettings() {
  const { data, error, isFetching, isFetched } = useApiQuery<
    CustomFormatProfileResource[]
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
    <PageContent title={translate('CustomFormatProfiles')}>
      <SettingsToolbar showSave={false} />

      <PageContentBody>
        {/* Phase 20 Plan 20-02 — route-load testid (D-18 selector strategy). */}
        <div data-testid="settings-customformatprofiles-page">
          <DndProvider options={HTML5toTouch}>
            <FieldSet legend={translate('CustomFormatProfiles')}>
              <PageSectionContent
                errorMessage={translate('CustomFormatProfilesLoadError')}
                error={error}
                isFetching={isFetching}
                isPopulated={isFetched}
              >
                <div className={styles.customFormatProfiles}>
                  {sortedItems.map((item) => {
                    return (
                      <CustomFormatProfile
                        key={item.id}
                        {...item}
                        onEditPress={handleEditPress}
                      />
                    );
                  })}

                  <Card
                    className={styles.addCustomFormatProfile}
                    onPress={handleAddPress}
                  >
                    <div className={styles.center}>
                      <Icon name={icons.ADD} size={45} />
                    </div>
                  </Card>
                </div>

                <EditCustomFormatProfileModal
                  id={editId}
                  isOpen={isEditModalOpen}
                  onModalClose={handleModalClose}
                />
              </PageSectionContent>
            </FieldSet>
          </DndProvider>
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default CustomFormatProfileSettings;
