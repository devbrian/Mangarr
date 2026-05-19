// Sonarr divergence: NEW manga sibling per Phase 25.1 D-02 — see
// 25.1-SUMMARY.md once Plan 25.1-03 lands.
// Role-match analog: frontend/src/AddSeries/ImportSeries/Import/ImportSeries.tsx
// (RR v6 syntax in upstream; this port adapts to RR v5 per L-RR6).
//
// Manga sibling preserves Sonarr's structural shape:
//   - <SelectProvider items={items}> outermost so per-row select checkboxes
//     in <ImportMangaRow> and the sticky footer can subscribe to a single
//     selection store
//   - <PageContentFooter> sticks to the viewport bottom because it lives
//     OUTSIDE <PageContentBody> but INSIDE <PageContent>
//   - Empty-state uses <Alert kind={kinds.INFO}> with the path interpolated
//     into the translation key, not a bare <div>
// Manga sibling diverges from ImportSeries:
//   - Defaults read from `useAddMangaOption` per D-05' (NOT from any
//     RootFolderResource field — RootFolderResource has no Default*
//     fields in either Mangarr or Sonarr v5-develop per RESEARCH §6).
//   - 5-value MangaMonitor select (Phase 6 D-03) replaces 11-value
//     SeriesMonitor.
//   - translationProfileId + customFormatProfileId replace
//     qualityProfileId (Phase 5 D-04).
//   - useLookupManga (existing hook in AddNewManga/useAddManga.ts)
//     replaces Sonarr's searchForSeries thunk per D-04. No new
//     backend endpoint consumed — frontend chained lookup against the
//     existing /api/v5/manga/lookup endpoint.
//
// useApiQuery cache key: per-row lookups cache by ['/manga/lookup',
// { term: folderName }]; navigating back to the same :rootFolderId
// does NOT re-fire the lookup chain (L-USEAPIQUERY-CACHE).
//
// Phase 8 cleanup: collapse with ImportSeries when AddSeries/ deletes.
import React, { useEffect, useMemo } from 'react';
import { useParams } from 'react-router-dom';
import { useAddMangaOption } from 'AddManga/addMangaOptionsStore';
import { SelectProvider } from 'App/Select/SelectContext';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { kinds } from 'Helpers/Props';
import { MangaMonitor } from 'Manga/Manga';
import useRootFolders, { UnmappedFolder } from 'RootFolder/useRootFolders';
import translate from 'Utilities/String/translate';
import {
  clearImportManga,
  ImportMangaItem,
  seedImportMangaItems,
} from '../importMangaStore';
import ImportMangaFooter from './ImportMangaFooter';
import ImportMangaRow from './ImportMangaRow';
import styles from './ImportManga.css';

// Columns mirror the Sonarr ImportSeries shape adapted for manga
// (Select + Folder + Monitor + TranslationProfile + CustomFormatProfile +
// Manga match column). The first column is the per-row selection
// checkbox (Sonarr's <VirtualTableSelectCell> peer — Mangarr uses the
// non-virtual TableSelectCell because the table is not virtualized).
const COLUMNS: Column[] = [
  {
    name: 'select',
    label: '',
    isVisible: true,
  },
  {
    name: 'folder',
    label: () => translate('Folder'),
    isVisible: true,
  },
  {
    name: 'monitor',
    label: () => translate('Monitor'),
    isVisible: true,
  },
  {
    name: 'translationProfile',
    label: () => translate('TranslationProfile'),
    isVisible: true,
  },
  {
    name: 'customFormatProfile',
    label: () => translate('CustomFormatProfile'),
    isVisible: true,
  },
  {
    name: 'manga',
    label: () => translate('Manga'),
    isVisible: true,
  },
];

function ImportManga() {
  const { rootFolderId: rootFolderIdParam } = useParams<{
    rootFolderId: string;
  }>();
  const rootFolderId = Number(rootFolderIdParam);

  const {
    data: rootFolders,
    isFetching,
    isFetched: rootFoldersFetched,
    error: rootFoldersError,
  } = useRootFolders();
  const rootFolder = rootFolders.find((rf) => rf.id === rootFolderId);
  // P-007 defensive `?? []` — rootFolder may be undefined while
  // useRootFolders() is still resolving.
  const unmappedFolders = useMemo<UnmappedFolder[]>(
    () => rootFolder?.unmappedFolders ?? [],
    [rootFolder]
  );

  // Per-row defaults sourced from addMangaOptionsStore per D-05'.
  const defaultMonitor = useAddMangaOption('monitor') as MangaMonitor;
  const defaultTranslationProfileId = useAddMangaOption('translationProfileId');
  const defaultCustomFormatProfileId = useAddMangaOption(
    'customFormatProfileId'
  );

  // Compose the per-row seed items deterministically from
  // unmappedFolders + the persisted defaults. Memoised so the seed
  // useEffect below only re-fires when one of the inputs actually
  // changes.
  const seedItems: ImportMangaItem[] = useMemo(() => {
    return unmappedFolders.map((uf) => ({
      id: uf.name,
      path: uf.path,
      name: uf.name,
      relativePath: uf.relativePath,
      monitor: defaultMonitor,
      translationProfileId: defaultTranslationProfileId,
      customFormatProfileId: defaultCustomFormatProfileId,
      selectedManga: undefined,
      hasSearched: false,
    }));
  }, [
    unmappedFolders,
    defaultMonitor,
    defaultTranslationProfileId,
    defaultCustomFormatProfileId,
  ]);

  // SelectProvider needs an items array with stable string `id` field —
  // each unmapped folder's name IS the row id, matching importMangaStore's
  // row-id contract.
  const selectItems = useMemo(
    () => unmappedFolders.map((uf) => ({ id: uf.name })),
    [unmappedFolders]
  );

  // Seed the store once per rootFolderId mount + on every defaults change.
  useEffect(() => {
    if (rootFoldersFetched && unmappedFolders.length > 0) {
      seedImportMangaItems(seedItems);
    }
  }, [rootFoldersFetched, unmappedFolders.length, seedItems]);

  // L-LOOKUP-RACE — clear the store on unmount or rootFolderId change.
  useEffect(() => {
    return () => {
      clearImportManga();
    };
  }, [rootFolderId]);

  if (isFetching && !rootFoldersFetched) {
    return (
      <PageContent title={translate('ImportManga')}>
        <PageContentBody>
          <div data-testid="import-manga-page">
            <LoadingIndicator />
          </div>
        </PageContentBody>
      </PageContent>
    );
  }

  if (!rootFolder) {
    return (
      <PageContent title={translate('ImportManga')}>
        <PageContentBody>
          <div className={styles.emptyState} data-testid="import-manga-page">
            {translate('RootFolderNotFound')}
          </div>
        </PageContentBody>
      </PageContent>
    );
  }

  return (
    <SelectProvider items={selectItems}>
      <PageContent title={translate('ImportManga')}>
        <PageContentBody>
          <div data-testid="import-manga-page" className={styles.scanContainer}>
            {!rootFoldersError && !!rootFoldersError === false ? null : null}

            {rootFoldersFetched && unmappedFolders.length === 0 ? (
              <Alert kind={kinds.INFO}>
                {translate('AllMangaInRootFolderHaveBeenImported', {
                  path: rootFolder.path,
                })}
              </Alert>
            ) : null}

            {unmappedFolders.length > 0 ? (
              <Table columns={COLUMNS}>
                <TableBody>
                  {unmappedFolders.map((uf) => (
                    <ImportMangaRow key={uf.path} unmappedFolder={uf} />
                  ))}
                </TableBody>
              </Table>
            ) : null}
          </div>
        </PageContentBody>

        {!rootFoldersError &&
        rootFoldersFetched &&
        unmappedFolders.length > 0 ? (
          <ImportMangaFooter rootFolderPath={rootFolder.path} />
        ) : null}
      </PageContent>
    </SelectProvider>
  );
}

export default ImportManga;
