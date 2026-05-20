import React, { useCallback, useRef, useState } from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import { icons } from 'Helpers/Props';
import SettingsToolbar from 'Settings/SettingsToolbar';
import {
  SaveCallback,
  SettingsStateChange,
} from 'typings/Settings/SettingsState';
import translate from 'Utilities/String/translate';
import ImportListExclusions from './ImportListExclusions/ImportListExclusions';
import ImportLists from './ImportLists/ImportLists';
import ManageImportListsModal from './ImportLists/Manage/ManageImportListsModal';
import ImportListOptions from './Options/ImportListOptions';

// Phase 26 Plan 26-05 (IL-05) — REWRITE of the Phase 15 Plan 15-12 placeholder
// page. The previous version displayed an Info alert reading "ImportLists not
// available yet for manga (v1.1+)" for nav-tree continuity. v1.1 substrate now
// ships: this page wires the translated tree mirroring
// `frontend/src/Settings/Indexers/IndexerSettings.tsx` (per RESEARCH §Q7
// parallel pattern).
//
// D-05 ENFORCED: Sonarr-canonical Settings → ImportLists has no Test-All hook
// nor SyncN-ow PageToolbarButton; this file imports neither, and the
// frontend/src/Settings/ImportLists/useImportLists.ts hook deliberately omits
// the matching TanStack mutation. Users trigger via System → Tasks UI or
// `POST /api/v5/command {name:"ImportListSync"}` directly.
//
// data-testid `settings-importlists-page` preserved from the prior page shape
// (Phase 18 D-18 selector strategy) so route-load fixtures continue to assert
// page mount.

function ImportListSettings() {
  const saveOptions = useRef<() => void>();

  const [isSaving, setIsSaving] = useState(false);
  const [hasPendingChanges, setHasPendingChanges] = useState(false);
  const [isManageImportListsModalOpen, setIsManageImportListsModalOpen] =
    useState(false);

  const handleSetChildSave = useCallback((saveCallback: SaveCallback) => {
    saveOptions.current = saveCallback;
  }, []);

  const handleChildStateChange = useCallback(
    ({ isSaving, hasPendingChanges }: SettingsStateChange) => {
      setIsSaving(isSaving);
      setHasPendingChanges(hasPendingChanges);
    },
    []
  );

  const handleManageImportListsPress = useCallback(() => {
    setIsManageImportListsModalOpen(true);
  }, []);

  const handleManageImportListsModalClose = useCallback(() => {
    setIsManageImportListsModalOpen(false);
  }, []);

  const handleSavePress = useCallback(() => {
    saveOptions.current?.();
  }, []);

  return (
    <PageContent title={translate('ImportListSettings')}>
      <SettingsToolbar
        isSaving={isSaving}
        hasPendingChanges={hasPendingChanges}
        additionalButtons={
          <>
            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('ManageImportLists')}
              iconName={icons.MANAGE}
              data-testid="settings-importlists-manage-button"
              onPress={handleManageImportListsPress}
            />
          </>
        }
        onSavePress={handleSavePress}
      />

      <PageContentBody>
        <div data-testid="settings-importlists-page">
          <ImportLists />

          <ImportListOptions
            setChildSave={handleSetChildSave}
            onChildStateChange={handleChildStateChange}
          />

          <ImportListExclusions />

          <ManageImportListsModal
            isOpen={isManageImportListsModalOpen}
            onModalClose={handleManageImportListsModalClose}
          />
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default ImportListSettings;
