import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import SettingsToolbar from 'Settings/SettingsToolbar';
import translate from 'Utilities/String/translate';
import Metadatas from './Metadata/Metadatas';

function MetadataSettings() {
  return (
    <PageContent title={translate('MetadataSettings')}>
      <SettingsToolbar showSave={false} />

      <PageContentBody>
        {/* Phase 20 Plan 20-02 — route-load testid (D-18 selector strategy). */}
        <div data-testid="settings-metadata-page">
          <Metadatas />
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default MetadataSettings;
