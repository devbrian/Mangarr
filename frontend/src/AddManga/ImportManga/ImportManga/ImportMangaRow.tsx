// Sonarr divergence: NEW manga sibling per Phase 25.1 D-02 — see
// 25.1-SUMMARY.md.
// Role-match analog: frontend/src/AddSeries/ImportSeries/Import/ImportSeriesRow.tsx
//
// Manga sibling preserves Sonarr's row contract:
//   - <TableSelectCell> per-row checkbox wired to useSelect<ImportMangaItem>()
//     (Mangarr uses TableSelectCell because the table is not virtualized;
//     Sonarr uses VirtualTableSelectCell)
//   - useExistingManga(selectedManga?.mangaDexId) drives the disabled state
//     so already-imported manga can't be re-imported via the bulk POST
//   - toggleDisabled on (!selectedManga || isExistingManga) keeps the
//     bulk-apply toolbar's selectedCount in sync with importable rows
//   - <ImportMangaSelectManga> dropdown replaces the bare {name} text so
//     the user can override an auto-matched manga
// Manga sibling diverges:
//   - 3 per-row selects (Monitor / TranslationProfile / CustomFormatProfile)
//     instead of Sonarr's 4. Phase 5 D-04 + Phase 8 audit + Phase 6 D-03.
//   - mangaDexId (string) identifies the manga instead of tvdbId (number).
//   - CustomFormatProfileSelectInput peer does NOT exist; inline-fetched
//     `/customformatprofile` values surface through EnhancedSelectInput
//     (matches the AddNewMangaModalContent.tsx:120-140 pattern).
//
// Phase 8 cleanup: collapse with ImportSeriesRow when AddSeries/ deletes.
import React, { useCallback, useEffect, useMemo } from 'react';
import { useAddMangaOption } from 'AddManga/addMangaOptionsStore';
import { useSelect } from 'App/Select/SelectContext';
import EnhancedSelectInput, {
  EnhancedSelectInputValue,
} from 'Components/Form/Select/EnhancedSelectInput';
import MonitorChaptersSelectInput from 'Components/Form/Select/MonitorChaptersSelectInput';
import TranslationProfileSelectInput from 'Components/Form/Select/TranslationProfileSelectInput';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import useDebounce from 'Helpers/Hooks/useDebounce';
import { MangaMonitor } from 'Manga/Manga';
import useExistingManga from 'Manga/useExistingManga';
import { UnmappedFolder } from 'RootFolder/useRootFolders';
import { EnhancedSelectInputChanged } from 'typings/inputs';
import { SelectStateInputProps } from 'typings/props';
import { useLookupManga } from '../../AddNewManga/useAddManga';
import {
  ImportMangaItem,
  removeFromLookupQueue,
  updateImportMangaItem,
  useImportMangaItem,
  useIsCurrentLookupQueueItem,
} from '../importMangaStore';
import ImportMangaSelectManga from './SelectManga/ImportMangaSelectManga';
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

  // Debounce the lookup term per L-USELOOKUPMANGA-DEBOUNCE (Sonarr upstream
  // verbatim).
  const debouncedTerm = useDebounce(rowId, rowId ? 300 : 0);

  // Per-row lookup; gated by `isCurrentLookupItem` so only the head of
  // the lookup queue fetches at any one time.
  const { isFetched, data: lookupResults } = useLookupManga(
    debouncedTerm,
    isCurrentLookupItem
  );

  // useExistingManga checks the /manga library query cache for an already-
  // imported manga whose mangaDexId matches the auto-matched (or user-
  // overridden) selectedManga. When true, the row is greyed out + disabled
  // so the bulk POST cannot fire on it.
  const isExistingManga = useExistingManga(item?.selectedManga?.mangaDexId);

  const { getIsSelected, toggleSelected, toggleDisabled } =
    useSelect<ImportMangaItem>();

  const handleSelectedChange = useCallback(
    ({ id, value, shiftKey }: SelectStateInputProps<string>) => {
      toggleSelected({
        id,
        isSelected: value,
        shiftKey,
      });
    },
    [toggleSelected]
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

  // Auto-select the top match on first lookup resolution (D-03). Guarded
  // by `hasSearched` so user overrides via the dropdown are not clobbered
  // by a delayed re-render.
  useEffect(() => {
    if (isFetched && lookupResults.length > 0 && item && !item.hasSearched) {
      updateImportMangaItem(rowId, {
        hasSearched: true,
        selectedManga: lookupResults[0],
      });
      removeFromLookupQueue(rowId);
    } else if (
      isFetched &&
      lookupResults.length === 0 &&
      item &&
      !item.hasSearched
    ) {
      updateImportMangaItem(rowId, { hasSearched: true });
      removeFromLookupQueue(rowId);
    }
  }, [isFetched, lookupResults, item, rowId]);

  // Disable the per-row checkbox when there's no match OR when the match
  // is already in the library. Sonarr's ImportSeriesRow does the same
  // (lines 60-62) — toggleDisabled keeps the bulk-apply toolbar's
  // selectedCount honest.
  useEffect(() => {
    toggleDisabled(rowId, !item?.selectedManga || isExistingManga);
  }, [rowId, item?.selectedManga, isExistingManga, toggleDisabled]);

  // Auto-select rows that have a fresh non-existing match. Sonarr does
  // the same (ImportSeriesRow lines 64-66) so the user doesn't have to
  // tick every checkbox manually on first scan.
  useEffect(() => {
    if (!item?.selectedManga || isExistingManga) {
      return;
    }
    toggleSelected({ id: rowId, isSelected: true, shiftKey: false });
  }, [rowId, item?.selectedManga, isExistingManga, toggleSelected]);

  // Pull store defaults again as a fallback when the row's item has not
  // yet been seeded into the store on first render.
  const fallbackMonitor = useAddMangaOption('monitor') as MangaMonitor;
  const fallbackTranslationProfileId = useAddMangaOption(
    'translationProfileId'
  );
  const fallbackCustomFormatProfileId = useAddMangaOption(
    'customFormatProfileId'
  );

  const monitor = item?.monitor ?? fallbackMonitor;
  const translationProfileId =
    item?.translationProfileId ?? fallbackTranslationProfileId;
  const customFormatProfileId =
    item?.customFormatProfileId ?? fallbackCustomFormatProfileId;

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
      <TableSelectCell<string>
        id={rowId}
        isSelected={getIsSelected(rowId)}
        isDisabled={!item?.selectedManga || isExistingManga}
        data-testid={`${rowTestId}-select`}
        onSelectedChange={handleSelectedChange}
      />

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
          <ImportMangaSelectManga id={rowId} />
        </div>
      </TableRowCell>
    </TableRow>
  );
}

export default ImportMangaRow;
