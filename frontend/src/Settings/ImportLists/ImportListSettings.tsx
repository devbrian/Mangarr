import React, { useCallback, useRef, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import { icons } from 'Helpers/Props';
import SettingsToolbar from 'Settings/SettingsToolbar';
import { testAllImportLists } from 'Store/Actions/settingsActions';
import {
  SaveCallback,
  SettingsStateChange,
} from 'typings/Settings/SettingsState';
import translate from 'Utilities/String/translate';
import ImportListExclusions from './ImportListExclusions/ImportListExclusions';
import ImportLists from './ImportLists/ImportLists';
import ManageImportListsModal from './ImportLists/Manage/ManageImportListsModal';
import ImportListOptions from './Options/ImportListOptions';

// Phase 27.1 Plan 27.1-05 verbatim Sonarr v5-develop port. D-02 amends
// Phase 26 D-05's "no Test-All hook" claim — testAllImportLists Redux thunk
// shipped Phase 26 substrate (importLists.js:53) but was never wired to a
// toolbar button. This file ships the wiring (TestAllLists + ManageImportLists
// PageToolbarButton pair) following the IndexerSettings.tsx peer with the
// Redux-canonical adaptation called out in PATTERNS.md (useDispatch +
// useSelector(state.settings.importLists.isTestingAll) replacing Indexers'
// useTestAllIndexers React Query hook). data-testid `settings-importlists-page`
// preserved from Phase 26 selector per A10.

function ImportListSettings() {
  const dispatch = useDispatch();
  const isTestingAll = useSelector(
    (state: AppState) => state.settings.importLists.isTestingAll
  );

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

  const handleTestAllImportListsPress = useCallback(() => {
    dispatch(testAllImportLists());
  }, [dispatch]);

  return (
    <PageContent title={translate('ImportListSettings')}>
      <SettingsToolbar
        isSaving={isSaving}
        hasPendingChanges={hasPendingChanges}
        additionalButtons={
          <>
            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('TestAllLists')}
              iconName={icons.TEST}
              isSpinning={isTestingAll}
              data-testid="settings-importlists-test-all-button"
              onPress={handleTestAllImportListsPress}
            />

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
