import React from 'react';
import Link from 'Components/Link/Link';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';
import SettingsToolbar from './SettingsToolbar';
import styles from './Settings.css';

// Phase 18 Plan 18-07: this page is the parent /settings route AND the canonical
// "settings tab navigator" (no separate left-rail toolbar exists for `/settings/*` in
// Mangarr's current shape — Sonarr's vertical sub-nav was never ported). The
// `settings-tab-*` testids therefore live on these <Link> rows. `Link` already
// propagates `data-testid` via `...otherProps` (data-testid-spec.md Wrapper-Component
// Sweep Ledger row 1). The settings-page-index container test-id wraps the body so
// SettingsPage PageObject has a single anchor.
function Settings() {
  return (
    <PageContent title={translate('Settings')}>
      <SettingsToolbar hasPendingChanges={false} />

      <PageContentBody>
        <div data-testid="settings-index-page">
          <Link
            className={styles.link}
            to="/settings/mediamanagement"
            data-testid="settings-tab-root-folders"
          >
            {translate('MediaManagement')}
          </Link>

          <div className={styles.summary}>
            {translate('MediaManagementSettingsSummary')}
          </div>

          {/* Sonarr divergence: per Phase 7 D-05 — Profiles tab renamed to "Translation Profiles" — see DIVERGENCE.md. */}
          <Link
            className={styles.link}
            to="/settings/profiles"
            data-testid="settings-tab-translation-profiles"
          >
            {translate('TranslationProfiles')}
          </Link>

          <div className={styles.summary}>
            {translate('TranslationProfilesSettingsSummary')}
          </div>

          {/* Sonarr divergence: NEW per Phase 7 D-05 — see DIVERGENCE.md. */}
          <Link
            className={styles.link}
            to="/settings/customformatprofiles"
            data-testid="settings-tab-custom-format-profiles"
          >
            {translate('CustomFormatProfiles')}
          </Link>

          <div className={styles.summary}>
            {translate('CustomFormatProfilesSettingsSummary')}
          </div>

          {/* Sonarr divergence: Phase 15 D-12 — Quality settings removed entirely; manga uses
              TranslationProfile + CustomFormatProfile per Phase 5. Phase 7 D-05 hidden via
              {false && (...)} JSX guard; Phase 15 Plan 15-07 deletes the guard atomically with
              the PageSidebar.tsx Quality nav spread-guard delete + the AppRoutes.tsx
              /settings/quality route delete + the frontend/src/Quality/ subtree delete. */}

          <Link
            className={styles.link}
            to="/settings/customformats"
            data-testid="settings-tab-custom-formats"
          >
            {translate('CustomFormats')}
          </Link>

          <div className={styles.summary}>
            {translate('CustomFormatsSettingsSummary')}
          </div>

          <Link
            className={styles.link}
            to="/settings/indexers"
            data-testid="settings-tab-indexers"
          >
            {translate('Indexers')}
          </Link>

          <div className={styles.summary}>
            {translate('IndexersSettingsSummary')}
          </div>

          <Link
            className={styles.link}
            to="/settings/downloadclients"
            data-testid="settings-tab-download-clients"
          >
            {translate('DownloadClients')}
          </Link>

          <div className={styles.summary}>
            {translate('DownloadClientsSettingsSummary')}
          </div>

          <Link
            className={styles.link}
            to="/settings/importlists"
            data-testid="settings-tab-import-lists"
          >
            {translate('ImportLists')}
          </Link>

          <div className={styles.summary}>
            {translate('ImportListsSettingsSummary')}
          </div>

          <Link
            className={styles.link}
            to="/settings/connect"
            data-testid="settings-tab-notifications"
          >
            {translate('Connect')}
          </Link>

          <div className={styles.summary}>
            {translate('ConnectSettingsSummary')}
          </div>

          <Link
            className={styles.link}
            to="/settings/metadata"
            data-testid="settings-tab-metadata"
          >
            {translate('Metadata')}
          </Link>

          <div className={styles.summary}>
            {translate('MetadataSettingsMangaSummary')}
          </div>

          <Link
            className={styles.link}
            to="/settings/metadatasource"
            data-testid="settings-tab-metadata-source"
          >
            {translate('MetadataSource')}
          </Link>

          <div className={styles.summary}>
            {translate('MetadataSourceSettingsMangaSummary')}
          </div>

          <Link
            className={styles.link}
            to="/settings/tags"
            data-testid="settings-tab-tags"
          >
            {translate('Tags')}
          </Link>

          <div className={styles.summary}>
            {translate('TagsSettingsSummary')}
          </div>

          <Link
            className={styles.link}
            to="/settings/general"
            data-testid="settings-tab-general"
          >
            {translate('General')}
          </Link>

          <div className={styles.summary}>
            {translate('GeneralSettingsSummary')}
          </div>

          <Link
            className={styles.link}
            to="/settings/ui"
            data-testid="settings-tab-ui"
          >
            {translate('Ui')}
          </Link>

          <div className={styles.summary}>
            {translate('UiSettingsSummary')}
          </div>
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default Settings;
