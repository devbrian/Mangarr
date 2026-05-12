import React, { useCallback, useRef, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import RootFolders from 'RootFolder/RootFolders';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import SettingsToolbar from 'Settings/SettingsToolbar';
import { useIsWindows } from 'System/Status/useSystemStatus';
import { InputChanged } from 'typings/inputs';
import { SettingsStateChange } from 'typings/Settings/SettingsState';
import translate from 'Utilities/String/translate';
// Sonarr divergence: Phase 15 Plan 15-12 fix-forward — TV `<Naming>` import removed.
// Plan 15-07's frontend cutover was supposed to delete the TV Naming subtree (per the
// Settings/MediaManagement/Naming/CLAUDE.md "Phase 8 cleanup" note) but the files
// survived. The TV `<Naming>` component called `/api/v5/settings/naming` which doesn't
// exist (V3 NamingSettingsController was deleted in Plan 15-06). The 404 caused a
// `TypeError: Cannot read properties of undefined (reading 'value')` crash that blanked
// the entire Settings → Media Management page (F-3 from 2026-05-08 smoke test).
//
// Manga naming lives at MangaNaming.tsx + /api/v5/config/manganaming (Phase 5 Plan 05-06)
// and works fine. The TV Naming child-state slot + handler are also dropped.
import MangaNaming from './Naming/MangaNaming';
import AddRootFolder from './RootFolder/AddRootFolder';
import {
  MediaManagementSettingsModel,
  useManageMediaManagementSettings,
} from './useMediaManagementSettings';

// Sonarr divergence: Phase 15 Plan 15-12 fix-forward — `episodeTitleRequiredOptions`
// and `downloadPropersAndRepacksOptions` consts removed alongside their FormGroups
// (manga backend doesn't expose those TV-only fields).
const rescanAfterRefreshOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: 'always',
    get value() {
      return translate('Always');
    },
  },
  {
    key: 'afterManual',
    get value() {
      return translate('AfterManualRefresh');
    },
  },
  {
    key: 'never',
    get value() {
      return translate('Never');
    },
  },
];

const fileDateOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: 'none',
    get value() {
      return translate('None');
    },
  },
  {
    key: 'localAirDate',
    get value() {
      return translate('LocalAirDate');
    },
  },
  {
    key: 'utcAirDate',
    get value() {
      return translate('UtcAirDate');
    },
  },
];

// Sonarr divergence: Phase 17.3 Plan 17.3-05 (D-06) — seasonPackUpgradeOptions
// + SeasonPackUpgrade FormGroup block removed (manga has no season packs).
// Paired with backend SeasonPackUpgradeType vertical delete + openapi.json schema delete.

function MediaManagement() {
  const showAdvancedSettings = useShowAdvancedSettings();
  const isWindows = useIsWindows();

  const {
    isFetching,
    isFetched: isPopulated,
    isSaving,
    error,
    settings,
    hasSettings,
    hasPendingChanges,
    validationErrors,
    validationWarnings,
    saveSettings: saveMediaManagementSettings,
    updateSetting,
  } = useManageMediaManagementSettings();

  // Sonarr divergence: Phase 15 Plan 15-12 fix-forward — TV `naming` child-state slot
  // dropped. Only manga naming remains (manga-naming child-state slot per Phase 7 D-05).
  const [mangaNaming, setMangaNaming] = useState<SettingsStateChange>({
    isSaving: false,
    hasPendingChanges: false,
  });

  const saveSettings = useRef<{
    mangaNaming: () => void;
  }>({
    mangaNaming: () => {},
  });

  const handleSetMangaNamingSave = useCallback(
    (saveCallback: () => void) => {
      saveSettings.current.mangaNaming = saveCallback;
    },
    []
  );

  const handleSavePress = useCallback(() => {
    saveMediaManagementSettings();
    saveSettings.current.mangaNaming();
  }, [saveMediaManagementSettings]);

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      updateSetting(
        change.name as keyof MediaManagementSettingsModel,
        change.value as MediaManagementSettingsModel[keyof MediaManagementSettingsModel]
      );
    },
    [updateSetting]
  );

  return (
    <PageContent title={translate('MediaManagementSettings')}>
      <SettingsToolbar
        isSaving={isSaving || mangaNaming.isSaving}
        hasPendingChanges={
          mangaNaming.hasPendingChanges || hasPendingChanges
        }
        onSavePress={handleSavePress}
      />

      <PageContentBody>
        {/* Sonarr divergence: Phase 7 D-05 manga sibling; Phase 15 Plan 15-12 fix-forward
            removed the TV `<Naming />` sibling that called the deleted /api/v5/settings/naming.
            Wires Phase 5 Plan 05-06 `/api/v5/config/manganaming` + presets endpoints. */}
        <MangaNaming
          setChildSave={handleSetMangaNamingSave}
          onChildStateChange={setMangaNaming}
        />

        {isFetching ? (
          <FieldSet legend={translate('NamingSettings')}>
            <LoadingIndicator />
          </FieldSet>
        ) : null}

        {!isFetching && error ? (
          <FieldSet legend={translate('NamingSettings')}>
            <Alert kind={kinds.DANGER}>
              {translate('MediaManagementSettingsLoadError')}
            </Alert>
          </FieldSet>
        ) : null}

        {hasSettings && isPopulated && !error ? (
          <Form
            id="mediaManagementSettings"
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            {showAdvancedSettings ? (
              <FieldSet legend={translate('Folders')}>
                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('CreateEmptyMangaFolders')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="createEmptySeriesFolders"
                    helpText={translate('CreateEmptyMangaFoldersHelpText')}
                    onChange={handleInputChange}
                    {...settings.createEmptySeriesFolders}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('DeleteEmptyFolders')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="deleteEmptyFolders"
                    helpText={translate('DeleteEmptyMangaFoldersHelpText')}
                    onChange={handleInputChange}
                    {...settings.deleteEmptyFolders}
                  />
                </FormGroup>
              </FieldSet>
            ) : null}

            {showAdvancedSettings ? (
              <FieldSet legend={translate('Importing')}>
                {/* Sonarr divergence: Phase 15 Plan 15-12 fix-forward — EpisodeTitleRequired
                    field removed. Manga backend MediaManagementSettingsResource doesn't
                    expose this TV-only setting (chapter titles aren't required-or-not in
                    the same way episode titles are); reading settings.episodeTitleRequired
                    .value crashed the form. Phase 8 cleanup: re-introduce a manga-shape
                    equivalent if the chapter-naming flow needs one. */}
                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('SkipFreeSpaceCheck')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="skipFreeSpaceCheckWhenImporting"
                    helpText={translate('SkipFreeSpaceCheckHelpText')}
                    onChange={handleInputChange}
                    {...settings.skipFreeSpaceCheckWhenImporting}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('MinimumFreeSpace')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.NUMBER}
                    unit="MB"
                    name="minimumFreeSpaceWhenImporting"
                    helpText={translate('MinimumFreeSpaceHelpText')}
                    onChange={handleInputChange}
                    {...settings.minimumFreeSpaceWhenImporting}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>
                    {translate('UseHardlinksInsteadOfCopy')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="copyUsingHardlinks"
                    helpText={translate('CopyUsingHardlinksMangaHelpText')}
                    helpTextWarning={translate(
                      'CopyUsingHardlinksHelpTextWarning'
                    )}
                    onChange={handleInputChange}
                    {...settings.copyUsingHardlinks}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('ImportUsingScript')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="useScriptImport"
                    helpText={translate('ImportUsingScriptHelpText')}
                    onChange={handleInputChange}
                    {...settings.useScriptImport}
                  />
                </FormGroup>

                {settings.useScriptImport.value ? (
                  <FormGroup
                    advancedSettings={showAdvancedSettings}
                    isAdvanced={true}
                  >
                    <FormLabel>{translate('ImportScriptPath')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.PATH}
                      includeFiles={true}
                      name="scriptImportPath"
                      helpText={translate('ImportScriptPathHelpText')}
                      onChange={handleInputChange}
                      {...settings.scriptImportPath}
                    />
                  </FormGroup>
                ) : null}

                <FormGroup size={sizes.MEDIUM}>
                  <FormLabel>{translate('ImportExtraFiles')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="importExtraFiles"
                    helpText={translate('ImportExtraFilesChapterHelpText')}
                    onChange={handleInputChange}
                    {...settings.importExtraFiles}
                  />
                </FormGroup>

                {settings.importExtraFiles.value ? (
                  <FormGroup
                    advancedSettings={showAdvancedSettings}
                    isAdvanced={true}
                  >
                    <FormLabel>{translate('ImportExtraFiles')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="extraFileExtensions"
                      helpTexts={[
                        translate('ExtraFileExtensionsHelpText'),
                        translate('ExtraFileExtensionsHelpTextsExamples'),
                      ]}
                      onChange={handleInputChange}
                      {...settings.extraFileExtensions}
                    />
                  </FormGroup>
                ) : null}

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>{translate('UserRejectedExtensions')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.TEXT}
                    name="userRejectedExtensions"
                    helpTexts={[
                      translate('UserRejectedExtensionsHelpText'),
                      translate('UserRejectedExtensionsTextsExamples'),
                    ]}
                    onChange={handleInputChange}
                    {...settings.userRejectedExtensions}
                  />
                </FormGroup>

                {/* Sonarr divergence: Phase 17.3 Plan 17.3-05 (D-06) — SeasonPackUpgrade
                    FormGroup + Threshold sub-form REMOVED (manga has no season packs).
                    Paired with backend SeasonPackUpgradeType vertical delete + openapi.json
                    schema delete. */}
              </FieldSet>
            ) : null}

            <FieldSet legend={translate('FileManagement')}>
              <FormGroup size={sizes.MEDIUM}>
                <FormLabel>{translate('UnmonitorDeletedChapters')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="autoUnmonitorPreviouslyDownloadedEpisodes"
                  helpText={translate('UnmonitorDeletedChaptersHelpText')}
                  onChange={handleInputChange}
                  {...settings.autoUnmonitorPreviouslyDownloadedEpisodes}
                />
              </FormGroup>

              {/* Sonarr divergence: Phase 15 Plan 15-12 fix-forward — DownloadPropersAndRepacks
                  field removed. Manga backend doesn't expose this TV-only setting
                  (Propers/Repacks are TV-shape concepts; manga uses CustomFormatProfile
                  for upgrade decisions); reading settings.downloadPropersAndRepacks.value
                  crashed the form. Phase 8 cleanup: route through CustomFormatProfile UI. */}

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
                size={sizes.MEDIUM}
              >
                <FormLabel>{translate('AnalyseVideoFiles')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableMediaInfo"
                  helpText={translate('AnalyseVideoFilesHelpText')}
                  onChange={handleInputChange}
                  {...settings.enableMediaInfo}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
              >
                <FormLabel>
                  {translate('RescanMangaFolderAfterRefresh')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="rescanAfterRefresh"
                  helpText={translate('RescanAfterRefreshMangaHelpText')}
                  helpTextWarning={translate(
                    'RescanAfterRefreshHelpTextWarning'
                  )}
                  values={rescanAfterRefreshOptions}
                  onChange={handleInputChange}
                  {...settings.rescanAfterRefresh}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
              >
                <FormLabel>{translate('ChangeFileDate')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="fileDate"
                  helpText={translate('ChangeFileDateHelpText')}
                  values={fileDateOptions}
                  onChange={handleInputChange}
                  {...settings.fileDate}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
              >
                <FormLabel>{translate('RecyclingBin')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.PATH}
                  name="recycleBin"
                  helpText={translate('RecyclingBinHelpText')}
                  includeFiles={false}
                  onChange={handleInputChange}
                  {...settings.recycleBin}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
              >
                <FormLabel>{translate('RecyclingBinCleanup')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="recycleBinCleanupDays"
                  helpText={translate('RecyclingBinCleanupHelpText')}
                  helpTextWarning={translate(
                    'RecyclingBinCleanupHelpTextWarning'
                  )}
                  min={0}
                  onChange={handleInputChange}
                  {...settings.recycleBinCleanupDays}
                />
              </FormGroup>
            </FieldSet>

            {showAdvancedSettings && !isWindows ? (
              <FieldSet legend={translate('Permissions')}>
                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('SetPermissions')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="setPermissionsLinux"
                    helpText={translate('SetPermissionsLinuxHelpText')}
                    helpTextWarning={translate(
                      'SetPermissionsLinuxHelpTextWarning'
                    )}
                    onChange={handleInputChange}
                    {...settings.setPermissionsLinux}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>{translate('ChmodFolder')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.UMASK}
                    name="chmodFolder"
                    helpText={translate('ChmodFolderHelpText')}
                    helpTextWarning={translate('ChmodFolderHelpTextWarning')}
                    onChange={handleInputChange}
                    {...settings.chmodFolder}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>{translate('ChownGroup')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.TEXT}
                    name="chownGroup"
                    helpText={translate('ChownGroupHelpText')}
                    helpTextWarning={translate('ChownGroupHelpTextWarning')}
                    onChange={handleInputChange}
                    {...settings.chownGroup}
                  />
                </FormGroup>
              </FieldSet>
            ) : null}
          </Form>
        ) : null}

        <FieldSet legend={translate('RootFolders')}>
          <RootFolders />
          <AddRootFolder />
        </FieldSet>
      </PageContentBody>
    </PageContent>
  );
}

export default MediaManagement;
