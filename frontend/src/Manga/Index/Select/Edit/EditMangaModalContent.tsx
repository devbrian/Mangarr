import React, { useCallback, useState } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './EditMangaModalContent.css';

// Sonarr divergence: Phase 17.3 Plan 17.3-11 (D-14 / D-13 cascade) — the
// inherited bulk-edit `SavePayload` originally carried `seriesType?: string`
// and `seasonFolder?: boolean` form fields. Plan 17.3-07 D-13 trim removed
// `seriesType` and `seasonFolder` from `Manga.ts` (per the v1 "ships free of
// TV vocabulary" lock — manga has no series-type axis and no season-folder
// concept per DOMAIN-02), so the bulk-edit modal no longer has a wire-shape
// destination for those fields. The two form controls + their state +
// payload branches + input-change cases have been removed in this commit.
// If a settable manga `type` ever becomes meaningful in the future, a fresh
// FormGroup will be authored against a real backing field.
interface SavePayload {
  monitored?: boolean;
  monitorNewItems?: string;
  qualityProfileId?: number;
  rootFolderPath?: string;
  moveFiles?: boolean;
}

export interface EditMangaModalContentProps {
  onSavePress(payload: object): void;
  onModalClose(): void;
}

const NO_CHANGE = 'noChange';

const monitoredOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: NO_CHANGE,
    get value() {
      return translate('NoChange');
    },
    isDisabled: true,
  },
  {
    key: 'monitored',
    get value() {
      return translate('Monitored');
    },
  },
  {
    key: 'unmonitored',
    get value() {
      return translate('Unmonitored');
    },
  },
];

// Sonarr divergence: Phase 17.3 Plan 17.3-11 (D-14 / D-13 cascade) — the
// inherited `seasonFolderOptions` constant was removed alongside the
// SeasonFolder bulk-edit form control (manga has no season-folder concept
// per DOMAIN-02).

function EditMangaModalContent(props: EditMangaModalContentProps) {
  const { onSavePress, onModalClose } = props;

  const [monitored, setMonitored] = useState(NO_CHANGE);
  const [monitorNewItems, setMonitorNewItems] = useState(NO_CHANGE);
  const [qualityProfileId, setQualityProfileId] = useState<string | number>(
    NO_CHANGE
  );
  // Sonarr divergence: Phase 17.3 Plan 17.3-11 (D-14 / D-13 cascade) — the
  // inherited `seriesType` + `seasonFolder` useState hooks were removed; the
  // backing fields no longer exist on Manga.ts per Plan 17.3-07 D-13 trim.
  const [rootFolderPath, setRootFolderPath] = useState(NO_CHANGE);
  const { selectedCount } = useSelect();

  const save = useCallback(
    (moveFiles: boolean) => {
      let hasChanges = false;
      const payload: SavePayload = {};

      if (monitored !== NO_CHANGE) {
        hasChanges = true;
        payload.monitored = monitored === 'monitored';
      }

      if (monitorNewItems !== NO_CHANGE) {
        hasChanges = true;
        payload.monitorNewItems = monitorNewItems;
      }

      if (qualityProfileId !== NO_CHANGE) {
        hasChanges = true;
        payload.qualityProfileId = qualityProfileId as number;
      }

      // Sonarr divergence: Phase 17.3 Plan 17.3-11 (D-14 / D-13 cascade) —
      // the inherited `seriesType` + `seasonFolder` payload-assignment
      // branches were removed; the backing fields no longer exist on
      // Manga.ts per Plan 17.3-07 D-13 trim.

      if (rootFolderPath !== NO_CHANGE) {
        hasChanges = true;
        payload.rootFolderPath = rootFolderPath;
        payload.moveFiles = moveFiles;
      }

      if (hasChanges) {
        onSavePress(payload);
      }

      onModalClose();
    },
    [
      monitored,
      monitorNewItems,
      qualityProfileId,
      rootFolderPath,
      onSavePress,
      onModalClose,
    ]
  );

  const onInputChange = useCallback(
    ({ name, value }: InputChanged) => {
      switch (name) {
        case 'monitored':
          setMonitored(value as string);
          break;
        case 'monitorNewItems':
          setMonitorNewItems(value as string);
          break;
        case 'qualityProfileId':
          setQualityProfileId(value as string);
          break;
        // Sonarr divergence: Phase 17.3 Plan 17.3-11 (D-14 / D-13 cascade) —
        // the inherited `seriesType` + `seasonFolder` input-change cases
        // were removed; their form controls no longer render.
        case 'rootFolderPath':
          setRootFolderPath(value as string);
          break;
        default:
          console.warn('EditMangaModalContent Unknown Input');
      }
    },
    [setMonitored]
  );

  // Sonarr divergence: Phase 17.3 Plan 17.3-03 (D-11) — the inherited "Move
  // Series" confirmation modal (ask whether to move files on disk when the
  // root folder changes) was a no-op stub and has been deleted. The real
  // "MoveManga to root folder" feature is tracked as a v1.x GitHub issue.
  // Until that ships, root-folder edits save with moveFiles=false (no on-disk move).
  const onSavePressWrapper = useCallback(() => {
    save(false);
  }, [save]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('EditSelectedSeries')}</ModalHeader>

      <ModalBody>
        <FormGroup>
          <FormLabel>{translate('Monitored')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="monitored"
            value={monitored}
            values={monitoredOptions}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('MonitorNewItems')}</FormLabel>

          <FormInputGroup
            type={inputTypes.MONITOR_NEW_ITEMS_SELECT}
            name="monitorNewItems"
            value={monitorNewItems}
            includeNoChange={true}
            includeNoChangeDisabled={false}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('QualityProfile')}</FormLabel>

          <FormInputGroup
            type={inputTypes.QUALITY_PROFILE_SELECT}
            name="qualityProfileId"
            value={qualityProfileId}
            includeNoChange={true}
            includeNoChangeDisabled={false}
            onChange={onInputChange}
          />
        </FormGroup>

        {/* Sonarr divergence: Phase 17.3 Plan 17.3-11 (D-14 / D-13 cascade) —
            the inherited "Manga Type" (SERIES_TYPE_SELECT) and "Season Folder"
            FormGroup blocks were deleted. Rationale: Plan 17.3-07 D-13 trim
            removed `seriesType` and `seasonFolder` from Manga.ts (manga has no
            series-type axis and no season-folder concept per DOMAIN-02), so
            these bulk-edit form controls had no manga-domain backing field.
            Per the v1 "ships free of TV vocabulary" lock, the controls are
            removed rather than rebound. */}

        <FormGroup>
          <FormLabel>{translate('RootFolder')}</FormLabel>

          <FormInputGroup
            type={inputTypes.ROOT_FOLDER_SELECT}
            name="rootFolderPath"
            value={rootFolderPath}
            includeNoChange={true}
            includeNoChangeDisabled={false}
            selectedValueOptions={{ includeFreeSpace: false }}
            helpText={translate('SeriesEditRootFolderHelpText')}
            onChange={onInputChange}
          />
        </FormGroup>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div className={styles.selected}>
          {translate('CountSeriesSelected', { count: selectedCount })}
        </div>

        <div>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <Button onPress={onSavePressWrapper}>
            {translate('ApplyChanges')}
          </Button>
        </div>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditMangaModalContent;
