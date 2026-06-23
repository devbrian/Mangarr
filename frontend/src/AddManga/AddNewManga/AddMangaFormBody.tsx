// Phase 42 Plan 42-07 — factored Add-Manga form body.
//
// Extracted verbatim from AddNewMangaModalContent.tsx (the <Form> body that was
// previously inline at lines 298-381) so the SAME field set + profile-fallback
// plumbing is reused by BOTH the single-add modal (AddNewMangaModalContent) and
// the count-only bulk-add modal (Discovery/AddTopX/AddTopXModalContent) per
// sketch 002 ("literally factor the existing form body"). No new form vocabulary.
//
// Self-contained on purpose: it reads the SAME addMangaOptionsStore both modals
// share (so defaults match single-add), runs the TranslationProfile +
// CustomFormatProfile useApiQuery fetches, and owns the default-profile fallback
// effects (quick-260608-gmm) so the Custom Format select never POSTs an orphan
// profileId 0. The caller passes the add-mutation error (for inline validation),
// the root-folder subfolder name, and — for the bulk modal — a one-line
// countSummary that renders in place of the single-add poster/overview.
import React, { ReactNode, useCallback, useEffect, useMemo } from 'react';
import {
  AddMangaOptions,
  setAddMangaOption,
  useAddMangaOptions,
} from 'AddManga/addMangaOptionsStore';
import MangaMonitoringOptionsPopoverContent from 'AddManga/MangaMonitoringOptionsPopoverContent';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import Icon from 'Components/Icon';
import Popover from 'Components/Tooltip/Popover';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons, inputTypes, tooltipPositions } from 'Helpers/Props';
import selectSettings from 'Store/Selectors/selectSettings';
import { useIsWindows } from 'System/Status/useSystemStatus';
import { InputChanged } from 'typings/inputs';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import translate from 'Utilities/String/translate';
import styles from './AddMangaFormBody.css';

// 5-value MangaMonitor enum (Phase 6 D-03) — UI-SPEC §Form / monitor labels.
// Moved here from AddNewMangaModalContent.tsx so both modals render the same set.
export const monitorValues: EnhancedSelectInputValue<string>[] = [
  {
    key: 'all',
    get value() {
      return translate('MonitorAllChapters');
    },
  },
  {
    key: 'future',
    get value() {
      return translate('MonitorFutureChapters');
    },
  },
  {
    key: 'missing',
    get value() {
      return translate('MonitorMissingChapters');
    },
  },
  {
    key: 'latest',
    get value() {
      return translate('MonitorLatestChapter');
    },
  },
  {
    key: 'none',
    get value() {
      return translate('MonitorNone');
    },
  },
];

interface ProfileResource {
  id: number;
  name?: string;
  // Backend-computed from Config.Default{Translation,CustomFormat}ProfileId (quick-260608-gmm).
  isDefault?: boolean;
}

export interface AddMangaFormBodyProps {
  // The add-mutation error (single-add useAddManga or bulk useDiscoveryBulkAdd) —
  // surfaced as inline field validation via getValidationFailures.
  addError?: ApiError | null;
  // Subfolder name previewed by the Root Folder select + help text. Single-add
  // passes the title; the bulk modal passes '' (no single title).
  rootFolderName?: string;
  // One-line count summary for the bulk modal; omitted by single-add.
  countSummary?: ReactNode;
}

// Self-contained form body shared by single-add + count-only bulk-add. Reads the
// shared addMangaOptionsStore directly; callers that need the current values for
// their submit payload / root-folder gate read the same store independently.
function AddMangaFormBody({
  addError,
  rootFolderName = '',
  countSummary,
}: AddMangaFormBodyProps) {
  const options = useAddMangaOptions();
  // isWindows reused for the RootFolderSelectInput rendering (it formats path
  // separators differently on Windows hosts).
  const isWindows = useIsWindows();

  // Fetch the TranslationProfile + CustomFormatProfile lists from Phase 5
  // V5 endpoints.
  const { data: translationProfilesData } = useApiQuery<ProfileResource[]>({
    path: '/translationprofile',
  });
  const { data: customFormatProfilesData } = useApiQuery<ProfileResource[]>({
    path: '/customformatprofile',
  });

  const translationProfileValues = useMemo<
    EnhancedSelectInputValue<number>[]
  >(() => {
    return (translationProfilesData ?? []).map((profile) => ({
      key: profile.id,
      value: profile.name ?? `Translation Profile ${profile.id}`,
    }));
  }, [translationProfilesData]);

  const customFormatProfileValues = useMemo<
    EnhancedSelectInputValue<number>[]
  >(() => {
    return (customFormatProfilesData ?? []).map((profile) => ({
      key: profile.id,
      value: profile.name ?? `Custom Format Profile ${profile.id}`,
    }));
  }, [customFormatProfilesData]);

  // Pre-select the default profile (the one flagged isDefault, else the first) whenever the
  // stored selection is unset (0) or points at a profile that no longer exists. The persisted
  // addMangaOptionsStore seeds both ids at 0 ("fall back to Config default"), and
  // EnhancedSelectInput does NOT write a default back — so without this the Custom Format select
  // rendered blank and POST sent customFormatProfileId 0 (orphan FK -> no CF scoring)
  // unless the user picked one by hand. quick-260608-gmm.
  useEffect(() => {
    if (!translationProfilesData?.length) {
      return;
    }
    const valid = translationProfilesData.some(
      (p) => p.id === options.translationProfileId
    );
    if (!valid) {
      const fallback =
        translationProfilesData.find((p) => p.isDefault) ??
        translationProfilesData[0];
      setAddMangaOption('translationProfileId', fallback.id);
    }
  }, [translationProfilesData, options.translationProfileId]);

  useEffect(() => {
    if (!customFormatProfilesData?.length) {
      return;
    }
    const valid = customFormatProfilesData.some(
      (p) => p.id === options.customFormatProfileId
    );
    if (!valid) {
      const fallback =
        customFormatProfilesData.find((p) => p.isDefault) ??
        customFormatProfilesData[0];
      setAddMangaOption('customFormatProfileId', fallback.id);
    }
  }, [customFormatProfilesData, options.customFormatProfileId]);

  const { settings, validationErrors, validationWarnings } = useMemo(() => {
    return {
      ...selectSettings(options, {}),
      ...getValidationFailures(addError),
    };
  }, [options, addError]);

  const {
    monitor,
    rootFolderPath,
    translationProfileId,
    customFormatProfileId,
    tags,
  } = settings;

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged<string | number | boolean | number[]>) => {
      setAddMangaOption(name as keyof AddMangaOptions, value);
    },
    []
  );

  return (
    <>
      {countSummary ? (
        <div className={styles.countSummary}>{countSummary}</div>
      ) : null}

      <Form
        validationErrors={validationErrors}
        validationWarnings={validationWarnings}
      >
        <FormGroup>
          <FormLabel>{translate('RootFolder')}</FormLabel>

          <FormInputGroup
            type={inputTypes.ROOT_FOLDER_SELECT}
            name="rootFolderPath"
            valueOptions={{
              mangaFolder: rootFolderName,
              isWindows,
            }}
            selectedValueOptions={{
              mangaFolder: rootFolderName,
              isWindows,
            }}
            helpText={translate('AddNewMangaRootFolderHelpText', {
              folder: rootFolderName,
            })}
            onChange={handleInputChange}
            {...rootFolderPath}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>
            {translate('Monitor')}

            <Popover
              anchor={<Icon className={styles.labelIcon} name={icons.INFO} />}
              title={translate('MonitoringOptions')}
              body={<MangaMonitoringOptionsPopoverContent />}
              position={tooltipPositions.RIGHT}
            />
          </FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="monitor"
            values={monitorValues}
            onChange={handleInputChange}
            {...monitor}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('TranslationProfile')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="translationProfileId"
            values={translationProfileValues}
            onChange={handleInputChange}
            {...translationProfileId}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('CustomFormatProfile')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="customFormatProfileId"
            values={customFormatProfileValues}
            onChange={handleInputChange}
            {...customFormatProfileId}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('Tags')}</FormLabel>

          <FormInputGroup
            type={inputTypes.TAG}
            name="tags"
            onChange={handleInputChange}
            {...tags}
          />
        </FormGroup>
      </Form>
    </>
  );
}

export default AddMangaFormBody;
