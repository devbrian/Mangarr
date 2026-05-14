import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import SettingsToolbar from 'Settings/SettingsToolbar';
import translate from 'Utilities/String/translate';
import Notifications from './Notifications/Notifications';

function NotificationSettings() {
  // Phase 18 Plan 18-07: SettingsToolbar.showSave={false} — there is no page-level
  // Save button. SettingsNotificationsPage PageObject documents the missing save
  // testid (per Task 1 deviation note in 18-07-PLAN.md).
  return (
    <PageContent title={translate('ConnectSettings')}>
      <SettingsToolbar showSave={false} />

      <PageContentBody>
        <div data-testid="settings-notifications-page">
          <Notifications />
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default NotificationSettings;
