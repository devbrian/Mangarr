// Sonarr divergence: NEW manga sibling per Phase 25.1 D-05 — see
// 25.1-SUMMARY.md.
// Role-match analog: frontend/src/AddSeries/ImportSeries/Import/ImportSeriesFooter.tsx
// (bulk-apply toolbar + Import button; RR-version-agnostic).
//
// Manga sibling preserves: Form + FormGroup + FormInputGroup wrapping
// pattern, 3 bulk-apply selects with `includeMixed={true}` for "(mixed)"
// rendering, SpinnerButton "Import" with selected-row-with-match count.
// Manga sibling diverges from ImportSeriesFooter:
//   - 3 selects (Monitor / TranslationProfile / CustomFormatProfile)
//     instead of Sonarr's 4 (Monitor / Quality / Language / SeriesType).
//     Phase 5 D-04 + Phase 8 audit.
//   - Import POSTs one /api/v5/manga per row via useAddManga.addManga
//     (NOT a batch endpoint — Sonarr also fires one POST per row).
//
// Phase 8 cleanup: collapse with ImportSeriesFooter when AddSeries/
// deletes.
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useHistory } from 'react-router-dom';
import EnhancedSelectInput, {
  EnhancedSelectInputValue,
} from 'Components/Form/Select/EnhancedSelectInput';
import MonitorChaptersSelectInput from 'Components/Form/Select/MonitorChaptersSelectInput';
import TranslationProfileSelectInput from 'Components/Form/Select/TranslationProfileSelectInput';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormLabel from 'Components/Form/FormLabel';
import SpinnerButton from 'Components/Link/SpinnerButton';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import { MangaMonitor } from 'Manga/Manga';
import { EnhancedSelectInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { useAddManga } from '../../AddNewManga/useAddManga';
import {
  ImportMangaItem,
  updateImportMangaItem,
  useImportMangaItems,
  useIsImportMangaProcessing,
  startProcessing,
  stopProcessing,
} from '../importMangaStore';
import styles from './ImportMangaFooter.css';

const MIXED_KEY = 'mixed';

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
  const { addManga, isAdding } = useAddManga();

  const [bulkMonitor, setBulkMonitor] = useState<string>(MIXED_KEY);
  const [bulkTranslationProfileId, setBulkTranslationProfileId] = useState<
    number | string
  >(MIXED_KEY);
  const [bulkCustomFormatProfileId, setBulkCustomFormatProfileId] = useState<
    number | string
  >(MIXED_KEY);

  const { data: customFormatProfilesData } = useApiQuery<ProfileResource[]>({
    path: '/customformatprofile',
  });
  const customFormatProfileValues = useMemo<
    EnhancedSelectInputValue<number | string>[]
  >(() => {
    const base: EnhancedSelectInputValue<number | string>[] = [
      {
        key: MIXED_KEY,
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

  const itemsWithMatch = useMemo(
    () => items.filter((i) => Boolean(i.selectedManga)),
    [items]
  );
  const importableCount = itemsWithMatch.length;

  const handleBulkMonitorChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string | number>) => {
      const next = String(value);
      setBulkMonitor(next);
      if (next === MIXED_KEY) {
        return;
      }
      // Apply to every row in the store — D-05: per-row inputs remain
      // editable after bulk-apply (we do NOT lock); the user can still
      // pick a per-row monitor value after firing this.
      for (const row of items) {
        updateImportMangaItem(row.id, { monitor: next as MangaMonitor });
      }
    },
    [items]
  );

  const handleBulkTranslationProfileChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string | number>) => {
      setBulkTranslationProfileId(value);
      if (value === MIXED_KEY) {
        return;
      }
      for (const row of items) {
        updateImportMangaItem(row.id, {
          translationProfileId: Number(value),
        });
      }
    },
    [items]
  );

  const handleBulkCustomFormatProfileChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string | number>) => {
      setBulkCustomFormatProfileId(value);
      if (value === MIXED_KEY) {
        return;
      }
      for (const row of items) {
        updateImportMangaItem(row.id, {
          customFormatProfileId: Number(value),
        });
      }
    },
    [items]
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
    if (importableCount === 0) {
      return;
    }
    setIsImporting(true);
    startProcessing();
    for (const row of itemsWithMatch) {
      submitOne(row);
    }
  }, [importableCount, itemsWithMatch, submitOne]);

  // Watch isAdding transitions to clear isImporting flag + redirect.
  // useAddManga.isAdding flips false after the final POST resolves; we
  // synchronise our spinner + redirect on that edge.
  useEffect(() => {
    if (isImporting && !isAdding) {
      setIsImporting(false);
      stopProcessing();
      history.push('/manga');
    }
  }, [isImporting, isAdding, history]);

  return (
    <div className={styles.footer} data-testid="import-manga-footer">
      <Form>
        <FormGroup>
          <FormLabel>{translate('Monitor')}</FormLabel>
          <div data-testid="import-manga-bulk-monitor">
            <MonitorChaptersSelectInput
              name="monitor"
              value={bulkMonitor}
              includeMixed={true}
              onChange={handleBulkMonitorChange}
            />
          </div>
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('TranslationProfile')}</FormLabel>
          <div data-testid="import-manga-bulk-translation-profile">
            <TranslationProfileSelectInput
              name="translationProfileId"
              value={bulkTranslationProfileId}
              includeMixed={true}
              onChange={handleBulkTranslationProfileChange}
            />
          </div>
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('CustomFormatProfile')}</FormLabel>
          <div data-testid="import-manga-bulk-custom-format-profile">
            <EnhancedSelectInput
              name="customFormatProfileId"
              value={bulkCustomFormatProfileId}
              values={customFormatProfileValues}
              onChange={handleBulkCustomFormatProfileChange}
            />
          </div>
        </FormGroup>
      </Form>

      <SpinnerButton
        kind={kinds.SUCCESS}
        isSpinning={isImporting || isProcessing}
        isDisabled={importableCount === 0}
        data-testid="import-manga-bulk-import-button"
        onPress={handleImportPress}
      >
        {translate('ImportCountManga', { count: importableCount })}
      </SpinnerButton>
    </div>
  );
}

export default ImportMangaFooter;
