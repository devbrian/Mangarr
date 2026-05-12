import React from 'react';
import Link from 'Components/Link/Link';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';
import SettingsToolbar from './SettingsToolbar';
import styles from './Settings.css';

function Settings() {
  return (
    <PageContent title={translate('Settings')}>
      <SettingsToolbar hasPendingChanges={false} />

      <PageContentBody>
        <Link className={styles.link} to="/settings/mediamanagement">
          {translate('MediaManagement')}
        </Link>

        <div className={styles.summary}>
          {translate('MediaManagementSettingsSummary')}
        </div>

        {/* Sonarr divergence: per Phase 7 D-05 — Profiles tab renamed to "Translation Profiles" — see DIVERGENCE.md. */}
        <Link className={styles.link} to="/settings/profiles">
          {translate('TranslationProfiles')}
        </Link>

        <div className={styles.summary}>
          {translate('TranslationProfilesSettingsSummary')}
        </div>

        {/* Sonarr divergence: NEW per Phase 7 D-05 — see DIVERGENCE.md. */}
        <Link className={styles.link} to="/settings/customformatprofiles">
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

        <Link className={styles.link} to="/settings/customformats">
          {translate('CustomFormats')}
        </Link>

        <div className={styles.summary}>
          {translate('CustomFormatsSettingsSummary')}
        </div>

        <Link className={styles.link} to="/settings/indexers">
          {translate('Indexers')}
        </Link>

        <div className={styles.summary}>
          {translate('IndexersSettingsSummary')}
        </div>

        <Link className={styles.link} to="/settings/downloadclients">
          {translate('DownloadClients')}
        </Link>

        <div className={styles.summary}>
          {translate('DownloadClientsSettingsSummary')}
        </div>

        <Link className={styles.link} to="/settings/importlists">
          {translate('ImportLists')}
        </Link>

        <div className={styles.summary}>
          {translate('ImportListsSettingsSummary')}
        </div>

        <Link className={styles.link} to="/settings/connect">
          {translate('Connect')}
        </Link>

        <div className={styles.summary}>
          {translate('ConnectSettingsSummary')}
        </div>

        <Link className={styles.link} to="/settings/metadata">
          {translate('Metadata')}
        </Link>

        <div className={styles.summary}>
          {translate('MetadataSettingsMangaSummary')}
        </div>

        <Link className={styles.link} to="/settings/metadatasource">
          {translate('MetadataSource')}
        </Link>

        <div className={styles.summary}>
          {translate('MetadataSourceSettingsMangaSummary')}
        </div>

        <Link className={styles.link} to="/settings/tags">
          {translate('Tags')}
        </Link>

        <div className={styles.summary}>{translate('TagsSettingsSummary')}</div>

        <Link className={styles.link} to="/settings/general">
          {translate('General')}
        </Link>

        <div className={styles.summary}>
          {translate('GeneralSettingsSummary')}
        </div>

        <Link className={styles.link} to="/settings/ui">
          {translate('Ui')}
        </Link>

        <div className={styles.summary}>{translate('UiSettingsSummary')}</div>
      </PageContentBody>
    </PageContent>
  );
}

export default Settings;
