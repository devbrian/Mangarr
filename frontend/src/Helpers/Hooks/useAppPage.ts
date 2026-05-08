// Sonarr divergence: Phase 15 Plan 15-12 — useSeries -> useManga rebind per
// cascade absorption (Plan 15-07 deleted Series subtree). The series-fetched-
// before-render gate is preserved verbatim; the field name 'seriesFetched' /
// 'seriesError' is kept at the consumer surface so AppContent / PageContentBody
// don't churn. Phase 8 cleanup: rename the surface fields when AppContent migrates.
import { useEffect, useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import { useTranslations } from 'App/useTranslations';
import useCommands from 'Commands/useCommands';
import useCustomFilters from 'Filters/useCustomFilters';
import { useInitializeLanguage } from 'Language/useLanguageName';
import { useLanguages } from 'Language/useLanguages';
import useManga from 'Manga/useManga';
import useIndexerFlags from 'Settings/Indexers/useIndexerFlags';
import { useQualityProfiles } from 'Settings/Profiles/Quality/useQualityProfiles';
import { useUiSettings } from 'Settings/UI/useUiSettings';
import { fetchCustomFilters } from 'Store/Actions/customFilterActions';
import { fetchImportLists } from 'Store/Actions/settingsActions';
import useSystemStatus from 'System/Status/useSystemStatus';
import useTags from 'Tags/useTags';
import { ApiError } from 'Utilities/Fetch/fetchJson';

const createErrorsSelector = ({
  customFiltersError,
  indexerFlagsError,
  systemStatusError,
  tagsError,
  translationsError,
  uiSettingsError,
  seriesError,
  qualityProfilesError,
  languagesError,
}: {
  customFiltersError: ApiError | null;
  indexerFlagsError: ApiError | null;
  systemStatusError: ApiError | null;
  tagsError: ApiError | null;
  translationsError: ApiError | null;
  uiSettingsError: ApiError | null;
  seriesError: ApiError | null;
  qualityProfilesError: ApiError | null;
  languagesError: ApiError | null;
}) =>
  createSelector(
    (state: AppState) => state.settings.importLists.error,
    (importListsError) => {
      const hasError = !!(
        customFiltersError ||
        seriesError ||
        uiSettingsError ||
        qualityProfilesError ||
        languagesError ||
        importListsError ||
        indexerFlagsError ||
        systemStatusError ||
        tagsError ||
        translationsError ||
        uiSettingsError
      );

      return {
        hasError,
        errors: {
          seriesError,
          customFiltersError,
          tagsError,
          uiSettingsError,
          qualityProfilesError,
          languagesError,
          importListsError,
          indexerFlagsError,
          systemStatusError,
          translationsError,
        },
      };
    }
  );

const useAppPage = () => {
  const dispatch = useDispatch();

  useCommands();
  useInitializeLanguage();

  const { isFetched: isCustomFiltersFetched, error: customFiltersError } =
    useCustomFilters();

  const { isFetched: isMangaFetched, error: mangaError } = useManga();

  const { isFetched: isSystemStatusFetched, error: systemStatusError } =
    useSystemStatus();

  const { isFetched: isTagsFetched, error: tagsError } = useTags();

  const { isFetched: isTranslationsFetched, error: translationsError } =
    useTranslations();

  const { isFetched: isUiSettingsFetched, error: uiSettingsError } =
    useUiSettings();

  const { isFetched: isQualityProfilesFetched, error: qualityProfilesError } =
    useQualityProfiles();

  const { isFetched: isLanguagesFetched, error: languagesError } =
    useLanguages();

  const { isFetched: isIndexerFlagsFetched, error: indexerFlagsError } =
    useIndexerFlags();

  const isAppStatePopulated = useSelector(
    (state: AppState) => state.settings.importLists.isPopulated
  );

  const isPopulated =
    isAppStatePopulated &&
    isCustomFiltersFetched &&
    isIndexerFlagsFetched &&
    isMangaFetched &&
    isSystemStatusFetched &&
    isTagsFetched &&
    isTranslationsFetched &&
    isUiSettingsFetched &&
    isQualityProfilesFetched &&
    isLanguagesFetched;

  const { hasError, errors } = useSelector(
    createErrorsSelector({
      customFiltersError,
      indexerFlagsError,
      seriesError: mangaError,
      systemStatusError,
      tagsError,
      translationsError,
      uiSettingsError,
      qualityProfilesError,
      languagesError,
    })
  );

  const isLocalStorageSupported = useMemo(() => {
    const key = 'sonarrTest';

    try {
      localStorage.setItem(key, key);
      localStorage.removeItem(key);

      return true;
    } catch {
      return false;
    }
  }, []);

  useEffect(() => {
    dispatch(fetchCustomFilters());
    dispatch(fetchImportLists());
  }, [dispatch]);

  return useMemo(() => {
    return { errors, hasError, isLocalStorageSupported, isPopulated };
  }, [errors, hasError, isLocalStorageSupported, isPopulated]);
};

export default useAppPage;
