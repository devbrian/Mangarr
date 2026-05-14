// Sonarr divergence: NEW manga sibling per Phase 7 D-04 — see DIVERGENCE.md.
// Role-match analog: frontend/src/AddSeries/AddNewSeries/AddNewSeriesModalContent.tsx
// (the side-panel form scaffolded verbatim with manga form-field divergence).
//
// Manga sibling preserves: ModalContent layout, Form/FormGroup/FormInputGroup
// component shell, selectSettings + getValidationFailures plumbing,
// SpinnerButton submit, ModalFooter SearchOnAdd row.
// Manga sibling diverges from AddNewSeriesModalContent (per D-04 + Phase 6 D-03/D-06):
//   * Drops the TV-only series-type field (anime/standard/daily — manga has
//     no equivalent; PROJECT.md MangaType is a separate concept reserved for
//     future plans).
//   * Drops the season-folder field (Volumes/Seasons OoS per PROJECT.md).
//   * Drops the cutoff-unmet search toggle (manga has no cutoff concept yet).
//   * Replaces the Sonarr quality-profile selector with a translationProfileId
//     selector reading from /api/v5/translationprofile (Phase 5 D-01).
//   * Adds customFormatProfileId reading from /api/v5/customformatprofile
//     (Phase 5 D-07) — adjacent to TranslationProfile.
//   * Renames the missing-search toggle to searchForMissingChapters
//     (Phase 6 D-06 SearchOnAdd toggle).
//   * Monitor dropdown ships the 5 manga values per Phase 6 D-03 + UI-SPEC
//     §Form / monitor labels (NOT the 11 Sonarr-side entries).
//
// Phase 8 cleanup: collapse with AddNewSeriesModalContent when AddSeries/ deletes.
import React, { useCallback, useMemo } from 'react';
import { AddMangaResult } from 'AddManga/AddManga';
import {
  AddMangaOptions,
  setAddMangaOption,
  useAddMangaOptions,
} from 'AddManga/addMangaOptionsStore';
import MangaMonitoringOptionsPopoverContent from 'AddManga/MangaMonitoringOptionsPopoverContent';
import { useAppDimension } from 'App/appStore';
import CheckInput from 'Components/Form/CheckInput';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import Icon from 'Components/Icon';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Popover from 'Components/Tooltip/Popover';
import { getValidationFailures } from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons, inputTypes, kinds, tooltipPositions } from 'Helpers/Props';
import MangaPoster from 'Manga/MangaPoster';
import selectSettings from 'Store/Selectors/selectSettings';
import { useIsWindows } from 'System/Status/useSystemStatus';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { useAddManga } from './useAddManga';
import styles from './AddNewMangaModalContent.css';

export interface AddNewMangaModalContentProps {
  manga: AddMangaResult;
  onModalClose: () => void;
}

// 5-value MangaMonitor enum (Phase 6 D-03) — UI-SPEC §Form / monitor labels.
const monitorValues: EnhancedSelectInputValue<string>[] = [
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
}

function AddNewMangaModalContent({
  manga,
  onModalClose,
}: AddNewMangaModalContentProps) {
  const { title, year, overview, images } = manga;
  const options = useAddMangaOptions();
  const isSmallScreen = useAppDimension('isSmallScreen');
  // isWindows reused for the RootFolderSelectInput rendering (it formats path
  // separators differently on Windows hosts).
  const isWindows = useIsWindows();

  const { isAdding, addError, addManga } = useAddManga();

  // Fetch the TranslationProfile + CustomFormatProfile lists from Phase 5
  // V5 endpoints. Defer enabling until the modal is open (it always is when
  // this component renders); no debounce needed.
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
    searchForMissingChapters,
    tags,
  } = settings;

  const handleInputChange = useCallback(
    ({ name, value }: InputChanged<string | number | boolean | number[]>) => {
      setAddMangaOption(name as keyof AddMangaOptions, value);
    },
    []
  );

  const handleAddMangaPress = useCallback(() => {
    // Bug fix new-manga-default-monitored (2026-05-08): send `monitored: true`
    // explicitly + nest the per-Chapter Monitor cascade fields under `addOptions`
    // so they survive backend MangaResource → Manga.AddOptions deserialization.
    // Without these, the backend MangaScannedHandler bypasses
    // SetChapterMonitoredStatus entirely and Manga.Monitored persists as false.
    // Backend MangaController.AddManga also defaults Monitored=true unless
    // AddOptions.Monitor=None — sending true from the UI is defense in depth.
    addManga({
      title: manga.title,
      titleSlug: manga.titleSlug,
      mangaDexId: manga.mangaDexId,
      aniListId: manga.aniListId,
      malId: manga.malId,
      rootFolderPath: rootFolderPath.value,
      monitored: monitor.value !== 'none',
      monitor: monitor.value,
      addOptions: {
        monitor: monitor.value,
        searchForMissingChapters: searchForMissingChapters.value,
      },
      translationProfileId: translationProfileId.value,
      customFormatProfileId: customFormatProfileId.value,
      tags: tags.value,
      searchForMissingChapters: searchForMissingChapters.value,
    });
  }, [
    manga,
    rootFolderPath,
    monitor,
    translationProfileId,
    customFormatProfileId,
    tags,
    searchForMissingChapters,
    addManga,
  ]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {title}

        {!title.includes(String(year)) && year ? (
          <span className={styles.year}>({year})</span>
        ) : null}
      </ModalHeader>

      <ModalBody>
        <div className={styles.container} data-testid="add-manga-modal">
          {isSmallScreen ? null : (
            <div className={styles.poster}>
              <MangaPoster
                className={styles.poster}
                images={images}
                size={250}
                title={title}
              />
            </div>
          )}

          <div className={styles.info}>
            {overview ? (
              <div className={styles.overview}>{overview}</div>
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
                    mangaFolder: title,
                    isWindows,
                  }}
                  selectedValueOptions={{
                    mangaFolder: title,
                    isWindows,
                  }}
                  helpText={translate('AddNewMangaRootFolderHelpText', {
                    folder: title,
                  })}
                  onChange={handleInputChange}
                  {...rootFolderPath}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  {translate('Monitor')}

                  <Popover
                    anchor={
                      <Icon className={styles.labelIcon} name={icons.INFO} />
                    }
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
          </div>
        </div>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div>
          <label className={styles.searchLabelContainer}>
            <span className={styles.searchLabel}>
              {translate('AddNewMangaSearchForMissingChapters')}
            </span>

            <CheckInput
              containerClassName={styles.searchInputContainer}
              className={styles.searchInput}
              name="searchForMissingChapters"
              onChange={handleInputChange}
              {...searchForMissingChapters}
            />
          </label>
        </div>

        <SpinnerButton
          className={styles.addButton}
          kind={kinds.SUCCESS}
          isSpinning={isAdding}
          data-testid="add-manga-modal-add-button"
          onPress={handleAddMangaPress}
        >
          {translate('AddMangaWithTitle', { title })}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddNewMangaModalContent;
