import React from 'react';
import { DndProvider } from 'react-dnd';
import { HTML5Backend } from 'react-dnd-html5-backend';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import ParseToolbarButton from 'Parse/ParseToolbarButton';
import SettingsToolbar from 'Settings/SettingsToolbar';
import translate from 'Utilities/String/translate';
import CustomFormats from './CustomFormats/CustomFormats';
import ManageCustomFormatsToolbarButton from './CustomFormats/Manage/ManageCustomFormatsToolbarButton';

// Sonarr divergence: per Phase 7 D-05 — mediaType filter added — see DIVERGENCE.md.
// quick-260608-gmm: the 'series'/'both' filter row (the "Mangarr-as-fork-of-Sonarr" toggle)
// was removed — the custom-format subsystem is manga-only now. CustomFormatMediaType is narrowed
// to 'manga' and <CustomFormats mediaType="manga" /> is rendered directly.
export type CustomFormatMediaType = 'manga';

function CustomFormatSettingsPage() {
  return (
    <PageContent title={translate('CustomFormatsSettings')}>
      <SettingsToolbar
        showSave={false}
        additionalButtons={
          <>
            <PageToolbarSeparator />

            <ParseToolbarButton />

            <ManageCustomFormatsToolbarButton />
          </>
        }
      />

      <PageContentBody>
        <div data-testid="settings-custom-formats-page">
          <DndProvider backend={HTML5Backend}>
            <CustomFormats mediaType="manga" />
          </DndProvider>
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default CustomFormatSettingsPage;
