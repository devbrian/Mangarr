import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import SettingsToolbar from 'Settings/SettingsToolbar';
import translate from 'Utilities/String/translate';
import MetadataSources from './MetadataSources/MetadataSources';

function MetadataSourceSettings() {
  // Phase 18 Plan 18-07: SettingsToolbar.showSave={false} — provider rows save inline
  // (no page-level Save button). SettingsMetadataSourcePage PageObject documents the
  // missing save testid.
  return (
    <PageContent title={translate('MetadataSourceSettings')}>
      <SettingsToolbar showSave={false} />

      <PageContentBody>
        <div data-testid="settings-metadata-source-page">
          <MetadataSources />
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default MetadataSourceSettings;
