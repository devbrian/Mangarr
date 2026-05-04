import React, { useCallback, useState } from 'react';
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
// Phase 5 D-10 already shipped the backend `?mediaType=` query parameter on
// `/api/v5/customformat`. v1 default = 'manga' (lean — manga is the canonical Mangarr
// content type). Toggle exposes 'series' / 'both' for users running Mangarr-as-fork-of-Sonarr
// legacy installs OR for users wanting to view both at once.
//
// Phase 8 cleanup: the toggle stays but the default may flip to 'manga'-only when TV pages delete.
export type CustomFormatMediaType = 'manga' | 'series' | 'both';

const MEDIA_TYPE_FILTER_OPTIONS: ReadonlyArray<{
  key: CustomFormatMediaType;
  labelKey: string;
}> = [
  { key: 'manga', labelKey: 'Manga' },
  { key: 'series', labelKey: 'Series' },
  { key: 'both', labelKey: 'Both' },
];

function CustomFormatSettingsPage() {
  const [mediaTypeFilter, setMediaTypeFilter] =
    useState<CustomFormatMediaType>('manga');

  const handleMediaTypeFilterChange = useCallback(
    (next: CustomFormatMediaType) => {
      setMediaTypeFilter(next);
    },
    []
  );

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
        {/* Sonarr divergence: NEW mediaType filter row per Phase 7 D-05 — see DIVERGENCE.md. */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '12px',
            margin: '12px 30px 0 30px',
          }}
        >
          <span style={{ fontWeight: 600 }}>{translate('Filter')}:</span>
          {MEDIA_TYPE_FILTER_OPTIONS.map(({ key, labelKey }) => {
            const isActive = mediaTypeFilter === key;
            return (
              <button
                key={key}
                type="button"
                onClick={() => handleMediaTypeFilterChange(key)}
                style={{
                  padding: '4px 12px',
                  cursor: 'pointer',
                  border: '1px solid var(--themeBlue, #5d9cec)',
                  background: isActive
                    ? 'var(--themeBlue, #5d9cec)'
                    : 'transparent',
                  color: isActive ? '#fff' : 'inherit',
                  borderRadius: '3px',
                  fontSize: '13px',
                }}
              >
                {translate(labelKey)}
              </button>
            );
          })}
        </div>

        <DndProvider backend={HTML5Backend}>
          <CustomFormats mediaType={mediaTypeFilter} />
        </DndProvider>
      </PageContentBody>
    </PageContent>
  );
}

export default CustomFormatSettingsPage;
