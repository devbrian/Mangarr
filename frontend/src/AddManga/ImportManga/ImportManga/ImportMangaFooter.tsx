// Sonarr divergence: NEW manga sibling per Phase 25.1 D-05 — see
// 25.1-SUMMARY.md.
// Role-match analog: frontend/src/AddSeries/ImportSeries/Import/ImportSeriesFooter.tsx
// (bulk-apply toolbar + Import button; RR-version-agnostic).
//
// Manga sibling preserves Sonarr's footer shape verbatim:
//   - <PageContentFooter> sticky shell (the existing shared primitive at
//     frontend/src/Components/Page/PageContentFooter.tsx)
//   - One <div className={styles.inputContainer}> per bulk select with a
//     <div className={styles.label}> + <Select> body
//   - selectedCount-aware bulk apply (writes only to SELECTED rows via
//     useSelect<ImportMangaItem>().getSelectedIds(), not every row)
//   - "Import {selectedCount} Manga" SpinnerButton + StartProcessing /
//     CancelProcessing buttons + ImportErrors popover for failures
//   - Computed "mixed" state per control (not a fixed MIXED sentinel) so
//     the dropdown renders "(mixed)" only when rows actually disagree
// Manga sibling diverges from ImportSeriesFooter:
//   - 3 bulk selects (Monitor / TranslationProfile / CustomFormatProfile)
//     instead of Sonarr's 4 (Monitor / Quality / Language / SeriesType).
//     Phase 5 D-04 + Phase 8 audit + Phase 6 D-03.
//   - Import POSTs one /api/v5/manga per row via useAddManga.addManga
//     (NOT a batch endpoint — Sonarr also fires one POST per row).
//
// Phase 8 cleanup: collapse with ImportSeriesFooter when AddSeries/ deletes.
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useHistory } from 'react-router-dom';
import { useSelect } from 'App/Select/SelectContext';
import EnhancedSelectInput, {
  EnhancedSelectInputValue,
} from 'Components/Form/Select/EnhancedSelectInput';
import MonitorChaptersSelectInput from 'Components/Form/Select/MonitorChaptersSelectInput';
import TranslationProfileSelectInput from 'Components/Form/Select/TranslationProfileSelectInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContentFooter from 'Components/Page/PageContentFooter';
import Popover from 'Components/Tooltip/Popover';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import { MangaMonitor } from 'Manga/Manga';
import { EnhancedSelectInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { useAddManga } from '../../AddNewManga/useAddManga';
import {
  ImportMangaItem,
  startProcessing,
  stopProcessing,
  updateImportMangaItem,
  useImportMangaItems,
  useIsImportMangaProcessing,
  useLookupQueueHasItems,
} from '../importMangaStore';
import styles from './ImportMangaFooter.css';

type MixedType = 'mixed';
const MIXED: MixedType = 'mixed';

interface ProfileResource {
  id: number;
  name?: string;
}

interface ImportMangaFooterProps {
  rootFolderPath: string;
}

function ImportMangaFooter({ rootFolderPath }: ImportMangaFooterProps) {
  const history = useHistory();
  const items = useImportMangaItems();
  const isProcessing = useIsImportMangaProcessing();
  const isLookingUpManga = useLookupQueueHasItems();
  const { addManga, isAdding, addError } = useAddManga();

  const { selectedCount, getSelectedIds } = useSelect<ImportMangaItem>();

  const [monitor, setMonitor] = useState<MangaMonitor | MixedType>(MIXED);
  const [translationProfileId, setTranslationProfileId] = useState<
    number | MixedType
  >(MIXED);
  const [customFormatProfileId, setCustomFormatProfileId] = useState<
    number | MixedType
  >(MIXED);

  const { data: customFormatProfilesData } = useApiQuery<ProfileResource[]>({
    path: '/customformatprofile',
  });
  const customFormatProfileValues = useMemo<
    EnhancedSelectInputValue<number | MixedType>[]
  >(() => {
    const base: EnhancedSelectInputValue<number | MixedType>[] = [
      {
        key: MIXED,
        get value() {
          return `(${translate('Mixed')})`;
        },
        isDisabled: true,
      },
    ];
    return base.concat(
      (customFormatProfilesData ?? []).map((profile) => ({
        key: profile.id,
        value: profile.name ?? `Custom Format Profile ${profile.id}`,
      }))
    );
  }, [customFormatProfilesData]);

  // Compute per-control mixed state from current items so the bulk
  // dropdown reflects whether per-row values actually agree.
  const {
    isMonitorMixed,
    isTranslationProfileMixed,
    isCustomFormatProfileMixed,
  } = useMemo(() => {
    let monitorMixed = false;
    let translationMixed = false;
    let customFormatMixed = false;
    if (items.length > 1) {
      const m0 = items[0].monitor;
      const t0 = items[0].translationProfileId;
      const c0 = items[0].customFormatProfileId;
      for (let i = 1; i < items.length; i++) {
        if (items[i].monitor !== m0) {
          monitorMixed = true;
        }
        if (items[i].translationProfileId !== t0) {
          translationMixed = true;
        }
        if (items[i].customFormatProfileId !== c0) {
          customFormatMixed = true;
        }
      }
    }
    return {
      isMonitorMixed: monitorMixed,
      isTranslationProfileMixed: translationMixed,
      isCustomFormatProfileMixed: customFormatMixed,
    };
  }, [items]);

  // Reconcile the displayed bulk value with the underlying items: when
  // mixed, force the (mixed) entry; when aligned, mirror the agreed-on
  // value. Sonarr does the same in its 4 useEffects.
  useEffect(() => {
    if (items.length === 0) {
      return;
    }
    if (isMonitorMixed && monitor !== MIXED) {
      setMonitor(MIXED);
    } else if (!isMonitorMixed && monitor !== items[0].monitor) {
      setMonitor(items[0].monitor);
    }
  }, [items, isMonitorMixed, monitor]);

  useEffect(() => {
    if (items.length === 0) {
      return;
    }
    if (isTranslationProfileMixed && translationProfileId !== MIXED) {
      setTranslationProfileId(MIXED);
    } else if (
      !isTranslationProfileMixed &&
      translationProfileId !== items[0].translationProfileId
    ) {
      setTranslationProfileId(items[0].translationProfileId);
    }
  }, [items, isTranslationProfileMixed, translationProfileId]);

  useEffect(() => {
    if (items.length === 0) {
      return;
    }
    if (isCustomFormatProfileMixed && customFormatProfileId !== MIXED) {
      setCustomFormatProfileId(MIXED);
    } else if (
      !isCustomFormatProfileMixed &&
      customFormatProfileId !== items[0].customFormatProfileId
    ) {
      setCustomFormatProfileId(items[0].customFormatProfileId);
    }
  }, [items, isCustomFormatProfileMixed, customFormatProfileId]);

  // Bulk-apply changes write only to SELECTED rows (per Sonarr) so the
  // user can curate which rows the bulk values apply to via the per-row
  // checkboxes.
  const handleBulkMonitorChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string | number>) => {
      const next = String(value);
      setMonitor(next as MangaMonitor | MixedType);
      if (next === MIXED) {
        return;
      }
      getSelectedIds().forEach((id) => {
        updateImportMangaItem(id, { monitor: next as MangaMonitor });
      });
    },
    [getSelectedIds]
  );

  const handleBulkTranslationProfileChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string | number>) => {
      setTranslationProfileId(
        value === MIXED ? MIXED : (Number(value) as number)
      );
      if (value === MIXED) {
        return;
      }
      getSelectedIds().forEach((id) => {
        updateImportMangaItem(id, { translationProfileId: Number(value) });
      });
    },
    [getSelectedIds]
  );

  const handleBulkCustomFormatProfileChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string | number>) => {
      setCustomFormatProfileId(
        value === MIXED ? MIXED : (Number(value) as number)
      );
      if (value === MIXED) {
        return;
      }
      getSelectedIds().forEach((id) => {
        updateImportMangaItem(id, { customFormatProfileId: Number(value) });
      });
    },
    [getSelectedIds]
  );

  const submitOne = useCallback(
    (row: ImportMangaItem) => {
      if (!row.selectedManga) {
        return;
      }
      const m = row.selectedManga;
      addManga({
        title: m.title,
        titleSlug: m.titleSlug,
        mangaDexId: m.mangaDexId,
        aniListId: m.aniListId,
        malId: m.malId,
        rootFolderPath,
        monitored: row.monitor !== 'none',
        monitor: row.monitor,
        addOptions: {
          monitor: row.monitor,
          searchForMissingChapters: false,
        },
        translationProfileId: row.translationProfileId,
        customFormatProfileId: row.customFormatProfileId,
        tags: [],
        searchForMissingChapters: false,
      });
    },
    [addManga, rootFolderPath]
  );

  const [isImporting, setIsImporting] = useState(false);

  const handleImportPress = useCallback(() => {
    const selectedIds = getSelectedIds();
    const selectedRows = items.filter(
      (i) => selectedIds.includes(i.id) && Boolean(i.selectedManga)
    );
    if (selectedRows.length === 0) {
      return;
    }
    setIsImporting(true);
    startProcessing();
    selectedRows.forEach(submitOne);
  }, [items, getSelectedIds, submitOne]);

  const handleLookupPress = useCallback(() => {
    startProcessing();
  }, []);

  const handleCancelLookupPress = useCallback(() => {
    stopProcessing();
  }, []);

  // Watch isAdding transitions to clear isImporting flag + redirect.
  // Redirect target is `/` (the Manga library index) per AppRoutes.tsx line 84
  // — Phase 15 Plan 15-07 flipped the root route from SeriesIndex to MangaIndex.
  useEffect(() => {
    if (isImporting && !isAdding) {
      setIsImporting(false);
      stopProcessing();
      history.push('/');
    }
  }, [isImporting, isAdding, history]);

  const hasUnsearchedItems =
    !isLookingUpManga && items.some((i) => !i.hasSearched);

  return (
    <PageContentFooter>
      <div className={styles.inputContainer}>
        <div className={styles.label}>{translate('Monitor')}</div>

        <div data-testid="import-manga-bulk-monitor">
          <MonitorChaptersSelectInput
            name="monitor"
            value={monitor}
            isDisabled={!selectedCount}
            includeMixed={isMonitorMixed}
            onChange={handleBulkMonitorChange}
          />
        </div>
      </div>

      <div className={styles.inputContainer}>
        <div className={styles.label}>{translate('TranslationProfile')}</div>

        <div data-testid="import-manga-bulk-translation-profile">
          <TranslationProfileSelectInput
            name="translationProfileId"
            value={translationProfileId}
            isDisabled={!selectedCount}
            includeMixed={isTranslationProfileMixed}
            onChange={handleBulkTranslationProfileChange}
          />
        </div>
      </div>

      <div className={styles.inputContainer}>
        <div className={styles.label}>{translate('CustomFormatProfile')}</div>

        <div data-testid="import-manga-bulk-custom-format-profile">
          <EnhancedSelectInput
            name="customFormatProfileId"
            value={customFormatProfileId}
            values={customFormatProfileValues}
            isDisabled={!selectedCount}
            onChange={handleBulkCustomFormatProfileChange}
          />
        </div>
      </div>

      <div>
        <div className={styles.label}>&nbsp;</div>

        <div className={styles.importButtonContainer}>
          <SpinnerButton
            className={styles.importButton}
            kind={kinds.PRIMARY}
            isSpinning={isImporting || isProcessing}
            isDisabled={!selectedCount || isLookingUpManga}
            data-testid="import-manga-bulk-import-button"
            onPress={handleImportPress}
          >
            {translate('ImportCountManga', { count: selectedCount })}
          </SpinnerButton>

          {isLookingUpManga ? (
            <Button
              className={styles.loadingButton}
              kind={kinds.WARNING}
              onPress={handleCancelLookupPress}
            >
              {translate('CancelProcessing')}
            </Button>
          ) : null}

          {hasUnsearchedItems ? (
            <Button
              className={styles.loadingButton}
              kind={kinds.SUCCESS}
              onPress={handleLookupPress}
            >
              {translate('StartProcessing')}
            </Button>
          ) : null}

          {isLookingUpManga ? (
            <LoadingIndicator className={styles.loading} size={24} />
          ) : null}

          {isLookingUpManga ? translate('ProcessingFolders') : null}

          {addError ? (
            <Popover
              anchor={
                <Icon
                  className={styles.importError}
                  name={icons.WARNING}
                  kind={kinds.WARNING}
                />
              }
              title={translate('ImportErrors')}
              body={
                <ul>
                  {Array.isArray(addError.statusBody) ? (
                    addError.statusBody.map((e, index) => {
                      return <li key={index}>{e.errorMessage}</li>;
                    })
                  ) : (
                    <li>{JSON.stringify(addError.statusBody)}</li>
                  )}
                </ul>
              }
              position={tooltipPositions.RIGHT}
            />
          ) : null}
        </div>
      </div>
    </PageContentFooter>
  );
}

export default ImportMangaFooter;
