import React, { useCallback, useRef, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import { icons } from 'Helpers/Props';
import SettingsToolbar from 'Settings/SettingsToolbar';
import { testAllDownloadClients } from 'Store/Actions/settingsActions';
import {
  SaveCallback,
  SettingsStateChange,
} from 'typings/Settings/SettingsState';
import translate from 'Utilities/String/translate';
import DownloadClients from './DownloadClients/DownloadClients';
import ManageDownloadClientsModal from './DownloadClients/Manage/ManageDownloadClientsModal';
import DownloadClientOptions from './Options/DownloadClientOptions';
import RemotePathMappings from './RemotePathMappings/RemotePathMappings';

function DownloadClientSettings() {
  const dispatch = useDispatch();
  const isTestingAll = useSelector(
    (state: AppState) => state.settings.downloadClients.isTestingAll
  );

  const saveOptions = useRef<() => void>();

  const [isSaving, setIsSaving] = useState(false);
  const [hasPendingChanges, setHasPendingChanges] = useState(false);
  const [
    isManageDownloadClientsModalOpen,
    setIsManageDownloadClientsModalOpen,
  ] = useState(false);

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

  const handleManageDownloadClientsPress = useCallback(() => {
    setIsManageDownloadClientsModalOpen(true);
  }, []);

  const handleManageDownloadClientsModalClose = useCallback(() => {
    setIsManageDownloadClientsModalOpen(false);
  }, []);

  const handleSavePress = useCallback(() => {
    saveOptions.current?.();
  }, []);

  const handleTestAllIndexersPress = useCallback(() => {
    dispatch(testAllDownloadClients());
  }, [dispatch]);

  // Phase 18 Plan 18-07: page container testid wraps the body for the
  // SettingsDownloadClientsPage PageObject. Save lives in the global SettingsToolbar.
  return (
    <PageContent title={translate('DownloadClientSettings')}>
      <SettingsToolbar
        isSaving={isSaving}
        hasPendingChanges={hasPendingChanges}
        additionalButtons={
          <>
            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('TestAllClients')}
              iconName={icons.TEST}
              isSpinning={isTestingAll}
              data-testid="settings-download-clients-test-all-button"
              onPress={handleTestAllIndexersPress}
            />

            <PageToolbarButton
              label={translate('ManageClients')}
              iconName={icons.MANAGE}
              data-testid="settings-download-clients-manage-button"
              onPress={handleManageDownloadClientsPress}
            />
          </>
        }
        onSavePress={handleSavePress}
      />

      <PageContentBody>
        <div data-testid="settings-download-clients-page">
          <DownloadClients />

          <DownloadClientOptions
            setChildSave={handleSetChildSave}
            onChildStateChange={handleChildStateChange}
          />

          <RemotePathMappings />

          <ManageDownloadClientsModal
            isOpen={isManageDownloadClientsModalOpen}
            onModalClose={handleManageDownloadClientsModalClose}
          />
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default DownloadClientSettings;
