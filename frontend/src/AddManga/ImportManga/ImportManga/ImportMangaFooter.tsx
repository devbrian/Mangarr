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
  const { addMangaAsync, addError } = useAddManga();

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

  // Per CodeRabbit review (PR #206) — derive mixed state from the SAME
  // row scope that bulk-apply writes to. When ≥1 row is selected, that's
  // the selected subset; when nothing is selected, the toolbar is
  // already disabled (isDisabled={!selectedCount}), but we still mirror
  // the global state so a fresh selection inherits a sensible starting
  // value rather than always landing on (mixed).
  const selectedIds = getSelectedIds();
  const rowsForBulk = useMemo(() => {
    if (selectedIds.length === 0) {
      return items;
    }
    const selectedIdSet = new Set(selectedIds);
    return items.filter((item) => selectedIdSet.has(item.id));
    // selectedIds.join makes the deps array stable when the underlying
    // selection set is unchanged but the array reference rotates.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [items, selectedIds.join('|')]);

  const {
    isMonitorMixed,
    isTranslationProfileMixed,
    isCustomFormatProfileMixed,
  } = useMemo(() => {
    let monitorMixed = false;
    let translationMixed = false;
    let customFormatMixed = false;
    if (rowsForBulk.length > 1) {
      const m0 = rowsForBulk[0].monitor;
      const t0 = rowsForBulk[0].translationProfileId;
      const c0 = rowsForBulk[0].customFormatProfileId;
      for (let i = 1; i < rowsForBulk.length; i++) {
        if (rowsForBulk[i].monitor !== m0) {
          monitorMixed = true;
        }
        if (rowsForBulk[i].translationProfileId !== t0) {
          translationMixed = true;
        }
        if (rowsForBulk[i].customFormatProfileId !== c0) {
          customFormatMixed = true;
        }
      }
    }
    return {
      isMonitorMixed: monitorMixed,
      isTranslationProfileMixed: translationMixed,
      isCustomFormatProfileMixed: customFormatMixed,
    };
  }, [rowsForBulk]);

  // Reconcile the displayed bulk value with the underlying rows: when
  // mixed, force the (mixed) entry; when aligned, mirror the agreed-on
  // value. Sonarr does the same in its 4 useEffects. Reads from
  // rowsForBulk so the displayed value reflects the bulk-apply target
  // (selected rows when there's a selection; all rows otherwise).
  useEffect(() => {
    if (rowsForBulk.length === 0) {
      return;
    }
    if (isMonitorMixed && monitor !== MIXED) {
      setMonitor(MIXED);
    } else if (!isMonitorMixed && monitor !== rowsForBulk[0].monitor) {
      setMonitor(rowsForBulk[0].monitor);
    }
  }, [rowsForBulk, isMonitorMixed, monitor]);

  useEffect(() => {
    if (rowsForBulk.length === 0) {
      return;
    }
    if (isTranslationProfileMixed && translationProfileId !== MIXED) {
      setTranslationProfileId(MIXED);
    } else if (
      !isTranslationProfileMixed &&
      translationProfileId !== rowsForBulk[0].translationProfileId
    ) {
      setTranslationProfileId(rowsForBulk[0].translationProfileId);
    }
  }, [rowsForBulk, isTranslationProfileMixed, translationProfileId]);

  useEffect(() => {
    if (rowsForBulk.length === 0) {
      return;
    }
    if (isCustomFormatProfileMixed && customFormatProfileId !== MIXED) {
      setCustomFormatProfileId(MIXED);
    } else if (
      !isCustomFormatProfileMixed &&
      customFormatProfileId !== rowsForBulk[0].customFormatProfileId
    ) {
      setCustomFormatProfileId(rowsForBulk[0].customFormatProfileId);
    }
  }, [rowsForBulk, isCustomFormatProfileMixed, customFormatProfileId]);

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
        return Promise.resolve(undefined);
      }
      const m = row.selectedManga;
      return addMangaAsync({
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
    [addMangaAsync, rootFolderPath]
  );

  const [isImporting, setIsImporting] = useState(false);

  const handleImportPress = useCallback(async () => {
    const selectedIds = getSelectedIds();
    const selectedRows = items.filter(
      (i) => selectedIds.includes(i.id) && Boolean(i.selectedManga)
    );
    if (selectedRows.length === 0) {
      return;
    }
    setIsImporting(true);
    startProcessing();
    // Promise.allSettled instead of fire-and-forget forEach so the
    // redirect waits for every selected row to settle. Per CodeRabbit
    // review (PR #206) — the prior `isImporting && !isAdding` gate fired
    // on the first row's settlement because `isPending` is a single
    // boolean, not a per-row tracker. allSettled (vs all) is intentional:
    // a partial failure should still redirect to the library page where
    // the partially-imported rows are visible, with the failure surfaced
    // via the addError popover that's already wired up below.
    try {
      await Promise.allSettled(selectedRows.map(submitOne));
    } finally {
      setIsImporting(false);
      stopProcessing();
      history.push('/');
    }
  }, [items, getSelectedIds, submitOne, history]);

  const handleLookupPress = useCallback(() => {
    startProcessing();
  }, []);

  const handleCancelLookupPress = useCallback(() => {
    stopProcessing();
  }, []);

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
