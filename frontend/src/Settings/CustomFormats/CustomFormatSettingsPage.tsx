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

  // Single stable handler for the filter button row — reads the target media
  // type off the button's data-media-type attribute so the .map() below does
  // not allocate a fresh arrow per render (react/jsx-no-bind).
  const handleMediaTypeButtonClick = useCallback(
    (event: React.MouseEvent<HTMLButtonElement>) => {
      const { mediaType } = event.currentTarget.dataset;
      if (mediaType) {
        setMediaTypeFilter(mediaType as CustomFormatMediaType);
      }
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
        <div data-testid="settings-custom-formats-page">
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
                  style={{
                    // WR-08 fix: var fallbacks must reflect the manga pink
                    // accent (Phase 7 D-06), not the original Sonarr cyan
                    // (#5d9cec). The full inline-style-to-CSS-module
                    // refactor is a follow-up cleanup; the immediate
                    // correctness concern is that if --themeBlue ever
                    // fails to resolve, the buttons render with the
                    // current accent rather than legacy Sonarr blue.
                    padding: '4px 12px',
                    cursor: 'pointer',
                    border: '1px solid var(--themeBlue, #f06292)',
                    background: isActive
                      ? 'var(--themeBlue, #f06292)'
                      : 'transparent',
                    color: isActive ? '#fff' : 'inherit',
                    borderRadius: '3px',
                    fontSize: '13px',
                  }}
                  data-media-type={key}
                  onClick={handleMediaTypeButtonClick}
                >
                  {translate(labelKey)}
                </button>
              );
            })}
          </div>

          <DndProvider backend={HTML5Backend}>
            <CustomFormats mediaType={mediaTypeFilter} />
          </DndProvider>
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default CustomFormatSettingsPage;
