import React from 'react';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import styles from './ErrorPage.css';

// Sonarr divergence: Phase 15 Plan 15-12 fix-forward — qualityProfilesError dropped
// from ErrorPageProps. Quality cascade deleted in Plan 15-03; bootstrap no longer
// fetches /api/v5/qualityprofile. Manga uses TranslationProfile + CustomFormatProfile.
interface ErrorPageProps {
  version: string;
  isLocalStorageSupported: boolean;
  translationsError: ApiError | null;
  seriesError: ApiError | null;
  customFiltersError: ApiError | null;
  tagsError: ApiError | null;
  uiSettingsError: ApiError | null;
  systemStatusError: ApiError | null;
}

function ErrorPage(props: ErrorPageProps) {
  const {
    version,
    isLocalStorageSupported,
    translationsError,
    seriesError,
    customFiltersError,
    tagsError,
    uiSettingsError,
    systemStatusError,
  } = props;

  let errorMessage = translate('FailedToLoadSonarr');

  if (!isLocalStorageSupported) {
    errorMessage = translate('LocalStorageIsNotSupported');
  } else if (translationsError) {
    errorMessage = getErrorMessage(
      translationsError,
      translate('FailedToLoadTranslationsFromApi')
    );
  } else if (seriesError) {
    errorMessage = getErrorMessage(
      seriesError,
      translate('FailedToLoadMangaFromApi')
    );
  } else if (customFiltersError) {
    errorMessage = getErrorMessage(
      customFiltersError,
      translate('FailedToLoadCustomFiltersFromApi')
    );
  } else if (tagsError) {
    errorMessage = getErrorMessage(
      tagsError,
      translate('FailedToLoadTagsFromApi')
    );
  } else if (uiSettingsError) {
    errorMessage = getErrorMessage(
      uiSettingsError,
      translate('FailedToLoadUiSettingsFromApi')
    );
  } else if (systemStatusError) {
    errorMessage = getErrorMessage(
      systemStatusError,
      translate('FailedToLoadSystemStatusFromApi')
    );
  }

  return (
    <div className={styles.page}>
      <div>{errorMessage}</div>

      <div className={styles.version}>
        {translate('VersionNumber', { version })}
      </div>
    </div>
  );
}

export default ErrorPage;
