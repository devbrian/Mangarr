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
import Indexers from './Indexers/Indexers';
import ManageIndexersModal from './Indexers/Manage/ManageIndexersModal';
import IndexerOptions from './Options/IndexerOptions';
import { useTestAllIndexers } from './useIndexers';

function IndexerSettings() {
  const { isTestingAllIndexers, testAllIndexers } = useTestAllIndexers();

  const saveOptions = useRef<() => void>();

  const [isSaving, setIsSaving] = useState(false);
  const [hasPendingChanges, setHasPendingChanges] = useState(false);
  const [isManageIndexersModalOpen, setIsManageIndexersModalOpen] =
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

  const handleManageIndexersPress = useCallback(() => {
    setIsManageIndexersModalOpen(true);
  }, []);

  const handleManageIndexersModalClose = useCallback(() => {
    setIsManageIndexersModalOpen(false);
  }, []);

  const handleSavePress = useCallback(() => {
    saveOptions.current?.();
  }, []);

  const handleTestAllIndexersPress = useCallback(() => {
    testAllIndexers();
  }, [testAllIndexers]);

  // Phase 18 Plan 18-07: page container testid wraps the body for the SettingsIndexersPage
  // PageObject. SettingsToolbar's primary Save button is the global save flow — the
  // additional ManageIndexers / TestAllIndexers PageToolbarButtons are wave-2 noise here.
  // PageToolbarButton extends Link, which propagates data-testid via ...otherProps.
  return (
    <PageContent title={translate('IndexerSettings')}>
      <SettingsToolbar
        isSaving={isSaving}
        hasPendingChanges={hasPendingChanges}
        additionalButtons={
          <>
            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('TestAllIndexers')}
              iconName={icons.TEST}
              isSpinning={isTestingAllIndexers}
              onPress={handleTestAllIndexersPress}
              data-testid="settings-indexers-test-all-button"
            />

            <PageToolbarButton
              label={translate('ManageIndexers')}
              iconName={icons.MANAGE}
              onPress={handleManageIndexersPress}
              data-testid="settings-indexers-manage-button"
            />
          </>
        }
        onSavePress={handleSavePress}
      />

      <PageContentBody>
        <div data-testid="settings-indexers-page">
          <Indexers />

          <IndexerOptions
            setChildSave={handleSetChildSave}
            onChildStateChange={handleChildStateChange}
          />

          <ManageIndexersModal
            isOpen={isManageIndexersModalOpen}
            onModalClose={handleManageIndexersModalClose}
          />
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default IndexerSettings;
