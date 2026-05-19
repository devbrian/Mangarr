// Sonarr divergence: NEW manga sibling per Phase 25.1 D-02 — see
// 25.1-SUMMARY.md.
// Role-match analog: frontend/src/AddSeries/ImportSeries/Import/ImportSeriesRow.tsx
// (RR v6 syntax in upstream; this Mangarr port uses RR v5 idioms — none
// of the row-level imports are RR-version-coupled, so no syntax adjustments).
//
// Manga sibling preserves: per-row useLookupManga chain debounced 300ms,
// auto-select top match on first lookup resolution (D-03), per-row
// state mutation via importMangaStore actions (NOT props lifting — the
// store IS the source of truth).
// Manga sibling diverges from ImportSeriesRow:
//   - 3 per-row selects (Monitor / TranslationProfile / CustomFormatProfile)
//     instead of Sonarr's 4 (Monitor / Quality / Language / SeriesType).
//     Phase 5 D-04 + Phase 8 audit + Phase 6 D-03.
//   - Manga match auto-select compares against AddMangaResult.title (not
//     Series title); the manga override is a future enhancement (popover
//     UI deferred to v1.2).
//
// CustomFormatProfileSelectInput peer does NOT exist in the codebase
// (verified via Glob frontend/src/Components/Form/Select/*ProfileSelectInput*.tsx
// at 25.1-02 plan time → only TranslationProfileSelectInput.tsx +
// QualityProfileSelectInput.tsx). This port uses inline-fetched
// `/customformatprofile` values surfaced through EnhancedSelectInput,
// matching the AddNewMangaModalContent.tsx:120-140 pattern verbatim.
//
// Phase 8 cleanup: collapse with ImportSeriesRow when AddSeries/ deletes.
import React, { useCallback, useEffect, useMemo } from 'react';
import { useAddMangaOption } from 'AddManga/addMangaOptionsStore';
import EnhancedSelectInput, {
  EnhancedSelectInputValue,
} from 'Components/Form/Select/EnhancedSelectInput';
import MonitorChaptersSelectInput from 'Components/Form/Select/MonitorChaptersSelectInput';
import TranslationProfileSelectInput from 'Components/Form/Select/TranslationProfileSelectInput';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import useDebounce from 'Helpers/Hooks/useDebounce';
import { MangaMonitor } from 'Manga/Manga';
import { UnmappedFolder } from 'RootFolder/useRootFolders';
import { EnhancedSelectInputChanged } from 'typings/inputs';
import { useLookupManga } from '../../AddNewManga/useAddManga';
import {
  removeFromLookupQueue,
  updateImportMangaItem,
  useImportMangaItem,
  useIsCurrentLookupQueueItem,
} from '../importMangaStore';
import styles from './ImportMangaRow.css';

interface ImportMangaRowProps {
  unmappedFolder: UnmappedFolder;
}

interface ProfileResource {
  id: number;
  name?: string;
}

function ImportMangaRow({ unmappedFolder }: ImportMangaRowProps) {
  const rowId = unmappedFolder.name;
  const item = useImportMangaItem(rowId);
  const isCurrentLookupItem = useIsCurrentLookupQueueItem(rowId);

  // Debounce the lookup term — `300ms` per L-USELOOKUPMANGA-DEBOUNCE
  // (RESEARCH §2.8 + Sonarr upstream verbatim). The 0ms immediate path
  // applies when folderName clears, but folderName here is fixed per
  // mount so this is essentially a "fire-once after 300ms" guard.
  const debouncedTerm = useDebounce(rowId, rowId ? 300 : 0);

  // Per-row lookup; gated by `isCurrentLookupItem` so only the head of
  // the lookup queue fetches at any one time (matches Sonarr's
  // searchForSeries thunk's queue semantics — prevents the MangaDex 40
  // req/min budget from being blown by a 20-row scan).
  const { isFetched, data: lookupResults } = useLookupManga(
    debouncedTerm,
    isCurrentLookupItem
  );

  // Custom Format profiles — inline fetch matching
  // AddNewMangaModalContent.tsx:120-140 because there's no
  // CustomFormatProfileSelectInput component peer in the codebase.
  const { data: customFormatProfilesData } = useApiQuery<ProfileResource[]>({
    path: '/customformatprofile',
  });
  const customFormatProfileValues = useMemo<
    EnhancedSelectInputValue<number>[]
  >(() => {
    return (customFormatProfilesData ?? []).map((profile) => ({
      key: profile.id,
      value: profile.name ?? `Custom Format Profile ${profile.id}`,
    }));
  }, [customFormatProfilesData]);

  // Auto-select the top match on first lookup resolution (D-03).
  // Guarded by `hasSearched` so user overrides via the dropdown are not
  // clobbered by a delayed re-render.
  useEffect(() => {
    if (
      isFetched &&
      lookupResults.length > 0 &&
      item &&
      !item.hasSearched
    ) {
      updateImportMangaItem(rowId, {
        hasSearched: true,
        selectedManga: lookupResults[0],
      });
      removeFromLookupQueue(rowId);
    } else if (isFetched && lookupResults.length === 0 && item && !item.hasSearched) {
      // No results — mark searched (silently) so the next row's lookup
      // can fire. hasSearched flips true, selectedManga stays undefined;
      // user must override via the dropdown to include this row in the
      // bulk import.
      updateImportMangaItem(rowId, { hasSearched: true });
      removeFromLookupQueue(rowId);
    }
  }, [isFetched, lookupResults, item, rowId]);

  // Pull store defaults again as a fallback when the row's item has not
  // yet been seeded into the store on first render.
  const fallbackMonitor = useAddMangaOption('monitor') as MangaMonitor;
  const fallbackTranslationProfileId = useAddMangaOption('translationProfileId');
  const fallbackCustomFormatProfileId = useAddMangaOption(
    'customFormatProfileId'
  );

  const monitor = item?.monitor ?? fallbackMonitor;
  const translationProfileId =
    item?.translationProfileId ?? fallbackTranslationProfileId;
  const customFormatProfileId =
    item?.customFormatProfileId ?? fallbackCustomFormatProfileId;
  const selectedMangaName = item?.selectedManga?.title;

  const handleMonitorChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string | number>) => {
      updateImportMangaItem(rowId, { monitor: value as MangaMonitor });
    },
    [rowId]
  );

  const handleTranslationProfileChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string | number>) => {
      updateImportMangaItem(rowId, {
        translationProfileId: Number(value),
      });
    },
    [rowId]
  );

  const handleCustomFormatProfileChange = useCallback(
    ({ value }: EnhancedSelectInputChanged<string | number>) => {
      updateImportMangaItem(rowId, {
        customFormatProfileId: Number(value),
      });
    },
    [rowId]
  );

  const rowTestId = `import-manga-row-${rowId}`;
  const monitorTestId = `import-manga-row-${rowId}-monitor`;
  const translationProfileTestId = `import-manga-row-${rowId}-translation-profile`;
  const customFormatProfileTestId = `import-manga-row-${rowId}-custom-format-profile`;
  const selectedMangaNameTestId = `import-manga-row-${rowId}-selected-manga-name`;

  return (
    <TableRow data-testid={rowTestId}>
      {/* Select column is currently a placeholder; the bulk-apply
          toolbar in ImportMangaFooter operates over rows with a populated
          selectedManga (per D-05 + Sonarr-canonical UX). Adding an
          explicit per-row checkbox is a v1.2 enhancement when the
          "skip without unselecting" UX surfaces. */}
      <TableRowCell className={styles.selectCell}>·</TableRowCell>

      <TableRowCell className={styles.folder}>
        {unmappedFolder.name}
      </TableRowCell>

      <TableRowCell className={styles.monitor}>
        <div data-testid={monitorTestId}>
          <MonitorChaptersSelectInput
            name="monitor"
            value={monitor}
            onChange={handleMonitorChange}
          />
        </div>
      </TableRowCell>

      <TableRowCell className={styles.translationProfile}>
        <div data-testid={translationProfileTestId}>
          <TranslationProfileSelectInput
            name="translationProfileId"
            value={translationProfileId}
            onChange={handleTranslationProfileChange}
          />
        </div>
      </TableRowCell>

      <TableRowCell className={styles.customFormatProfile}>
        <div data-testid={customFormatProfileTestId}>
          <EnhancedSelectInput
            name="customFormatProfileId"
            value={customFormatProfileId}
            values={customFormatProfileValues}
            onChange={handleCustomFormatProfileChange}
          />
        </div>
      </TableRowCell>

      <TableRowCell className={styles.manga}>
        <div data-testid={selectedMangaNameTestId} className={styles.mangaName}>
          {selectedMangaName ?? ''}
        </div>
      </TableRowCell>
    </TableRow>
  );
}

export default ImportMangaRow;
