// Sonarr divergence: NEW manga sibling per Phase 25.1 D-02 — see
// 25.1-SUMMARY.md once Plan 25.1-03 lands.
// Role-match analog: frontend/src/AddSeries/ImportSeries/SelectFolder/ImportSeriesSelectFolder.tsx
// (RR v6 syntax in upstream; this Mangarr port is RR-v5-naive — the row
// component owns its own click handler via react-router-dom Link).
//
// Manga sibling preserves: useRootFolders consumption, LoadingIndicator
// while fetching, Alert kind=DANGER on error, Table/TableBody render
// shape, per-row Link to /add/import/${id}.
// Manga sibling diverges from ImportSeriesSelectFolder:
//   - NO FileBrowserModal "Choose another folder" branch (CONTEXT.md
//     <deferred>: defer to v1.2 unless Root Folder + scan happy-path
//     proves insufficient during 25.1-02 UAT). NO disabled placeholder
//     either per L-CHOOSE-ANOTHER-FOLDER-BUTTON-VISUAL.
//   - Simpler row signal (path + free space + unmapped-folder count
//     badge) — Sonarr surfaces a "recent folders" list under the root
//     folder list that this port omits (the existing
//     /settings/mediamanagement Root Folders page already shows recent
//     folders if the user wants to see them).
//
// Phase 8 cleanup: collapse with ImportSeriesSelectFolder when AddSeries/
// deletes.
import React from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { kinds } from 'Helpers/Props';
import useRootFolders from 'RootFolder/useRootFolders';
import translate from 'Utilities/String/translate';
import ImportMangaSelectFolderRow from './ImportMangaSelectFolderRow';

// Column shape mirrors the existing Settings/RootFolders table (path +
// free space + unmapped-folder count) for visual familiarity. The
// `actions` column is omitted because rows are clickable navigation
// targets here, not edit-/delete-target rows.
const rootFolderColumns: Column[] = [
  {
    name: 'path',
    label: () => translate('Path'),
    isVisible: true,
  },
  {
    name: 'freeSpace',
    label: () => translate('FreeSpace'),
    isVisible: true,
  },
  {
    name: 'unmappedFolders',
    label: () => translate('UnmappedFolders'),
    isVisible: true,
  },
];

function ImportMangaSelectFolder() {
  const { isFetching, isFetched, error, data } = useRootFolders();

  if (isFetching && !isFetched) {
    return (
      <PageContent title={translate('ImportManga')}>
        <PageContentBody>
          <div data-testid="import-manga-select-folder-page">
            <LoadingIndicator />
          </div>
        </PageContentBody>
      </PageContent>
    );
  }

  if (!isFetching && !!error) {
    return (
      <PageContent title={translate('ImportManga')}>
        <PageContentBody>
          <div data-testid="import-manga-select-folder-page">
            <Alert kind={kinds.DANGER}>
              {translate('RootFoldersLoadError')}
            </Alert>
          </div>
        </PageContentBody>
      </PageContent>
    );
  }

  return (
    <PageContent title={translate('ImportManga')}>
      <PageContentBody>
        <div data-testid="import-manga-select-folder-page">
          <Table columns={rootFolderColumns}>
            <TableBody>
              {data.map((rootFolder) => {
                return (
                  <ImportMangaSelectFolderRow
                    key={rootFolder.id}
                    id={rootFolder.id}
                    path={rootFolder.path}
                    accessible={rootFolder.accessible}
                    freeSpace={rootFolder.freeSpace}
                    unmappedFolders={rootFolder.unmappedFolders}
                  />
                );
              })}
            </TableBody>
          </Table>
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default ImportMangaSelectFolder;
