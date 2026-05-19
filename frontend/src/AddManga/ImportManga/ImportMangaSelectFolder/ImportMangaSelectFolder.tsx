// Sonarr divergence: NEW manga sibling per Phase 25.1 D-02 — see
// 25.1-SUMMARY.md once Plan 25.1-03 lands.
// Role-match analog: frontend/src/AddSeries/ImportSeries/SelectFolder/ImportSeriesSelectFolder.tsx
//
// Manga sibling preserves Sonarr's visual shell verbatim:
//   - Centered "Import manga you already have" header
//   - 3-bullet tips block via InlineMarkdown
//   - <FieldSet legend={translate('RootFolders')}> wrapping the shared
//     <RootFolders /> component (the existing Settings/MediaManagement
//     table chrome — Path / FreeSpace / UnmappedFolders / delete-X)
//   - Primary <Button> with icons.DRIVE that opens <FileBrowserModal>
//     to add a new root folder and resolves to "Choose another folder"
//     when ≥1 root folder exists, "Start Import" otherwise
//   - <Alert kind=DANGER> rendering RootFolderResource validation errors
//     from useAddRootFolder.addError.statusBody
// Manga sibling diverges from ImportSeriesSelectFolder:
//   - Manga-specific copy keys (LibraryImportMangaHeader,
//     LibraryImportTipsMangaUseRootFolder, LibraryImportTipsChapterFilename)
//   - "Anime" / "manhwa" folder examples instead of TV-show examples
//
// Phase 8 cleanup: collapse with ImportSeriesSelectFolder when AddSeries/
// deletes.
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import FileBrowserModal from 'Components/FileBrowser/FileBrowserModal';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { icons, kinds, sizes } from 'Helpers/Props';
import RootFolders from 'RootFolder/RootFolders';
import useRootFolders, { useAddRootFolder } from 'RootFolder/useRootFolders';
import { useIsWindows } from 'System/Status/useSystemStatus';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './ImportMangaSelectFolder.css';

function ImportMangaSelectFolder() {
  const { isFetching, isFetched, error, data } = useRootFolders();
  const { addRootFolder, isAdding, addError } = useAddRootFolder();

  const isWindows = useIsWindows();

  const [isAddNewRootFolderModalOpen, setIsAddNewRootFolderModalOpen] =
    useState(false);

  const hasRootFolders = data.length > 0;
  const goodFolderExample = isWindows ? 'C:\\manga' : '/manga';
  const badFolderExample = isWindows
    ? 'C:\\manga\\one piece'
    : '/manga/one piece';

  const handleAddNewRootFolderPress = useCallback(() => {
    setIsAddNewRootFolderModalOpen(true);
  }, []);

  const handleAddRootFolderModalClose = useCallback(() => {
    setIsAddNewRootFolderModalOpen(false);
  }, []);

  const handleNewRootFolderSelect = useCallback(
    ({ value }: InputChanged<string>) => {
      addRootFolder({ path: value });
    },
    [addRootFolder]
  );

  // Early-return on loading + error so the `import-manga-select-folder-page`
  // testid only mounts on the SUCCESS branch. This matches the pre-debug-add-
  // import-ui-mismatch shape and Sonarr's canonical `ImportSeriesSelectFolder`
  // shape — the test contract is "when the testid is present, the row list
  // is rendered." If the testid lived on the outer div too, then
  // ImportMangaSelectFolderFixture.renders_root_folder_list could race the
  // useRootFolders() fetch and CountAsync 0 rows before the fetch resolved.
  if (isFetching && !isFetched) {
    return (
      <PageContent title={translate('ImportManga')}>
        <PageContentBody>
          <LoadingIndicator />
        </PageContentBody>
      </PageContent>
    );
  }

  if (!isFetching && error) {
    return (
      <PageContent title={translate('ImportManga')}>
        <PageContentBody>
          <Alert kind={kinds.DANGER}>{translate('RootFoldersLoadError')}</Alert>
        </PageContentBody>
      </PageContent>
    );
  }

  return (
    <PageContent title={translate('ImportManga')}>
      <PageContentBody>
        <div data-testid="import-manga-select-folder-page">
          <div className={styles.header}>
            {translate('LibraryImportMangaHeader')}
          </div>

          <div className={styles.tips}>
            {translate('LibraryImportTips')}
            <ul>
              <li className={styles.tip}>
                <InlineMarkdown
                  data={translate('LibraryImportTipsChapterFilename')}
                />
              </li>
              <li className={styles.tip}>
                <InlineMarkdown
                  data={translate('LibraryImportTipsMangaUseRootFolder', {
                    goodFolderExample,
                    badFolderExample,
                  })}
                />
              </li>
              <li className={styles.tip}>
                {translate('LibraryImportTipsDontUseDownloadsFolder')}
              </li>
            </ul>
          </div>

          {hasRootFolders ? (
            <div className={styles.recentFolders}>
              <FieldSet legend={translate('RootFolders')}>
                <RootFolders />
              </FieldSet>
            </div>
          ) : null}

          {!isAdding && addError ? (
            <Alert className={styles.addErrorAlert} kind={kinds.DANGER}>
              {translate('AddRootFolderError')}

              <ul>
                {Array.isArray(addError.statusBody) ? (
                  addError.statusBody.map((e, index) => {
                    return <li key={index}>{e.errorMessage}</li>;
                  })
                ) : (
                  <li>{JSON.stringify(addError.statusBody)}</li>
                )}
              </ul>
            </Alert>
          ) : null}

          <div className={hasRootFolders ? undefined : styles.startImport}>
            <Button
              kind={kinds.PRIMARY}
              size={sizes.LARGE}
              onPress={handleAddNewRootFolderPress}
            >
              <Icon className={styles.importButtonIcon} name={icons.DRIVE} />
              {hasRootFolders
                ? translate('ChooseAnotherFolder')
                : translate('StartImport')}
            </Button>
          </div>

          <FileBrowserModal
            isOpen={isAddNewRootFolderModalOpen}
            name="rootFolderPath"
            value=""
            onChange={handleNewRootFolderSelect}
            onModalClose={handleAddRootFolderModalClose}
          />
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default ImportMangaSelectFolder;
