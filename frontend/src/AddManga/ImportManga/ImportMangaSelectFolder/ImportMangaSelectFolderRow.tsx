// Sonarr divergence: NEW manga sibling per Phase 25.1 D-02 — see
// 25.1-SUMMARY.md.
// Role-match analog: frontend/src/AddSeries/ImportSeries/SelectFolder/ImportSeriesSelectFolderRow.tsx
// (RR v6 syntax in upstream; this Mangarr port uses react-router-dom Link
// per RR v5).
//
// Manga sibling preserves: Link-to-deep-link row navigation, path display,
// free-space cell, unmapped-folder count badge.
// Manga sibling diverges from ImportSeriesSelectFolderRow:
//   - No "Choose another folder" arbitrary-path mode (see
//     ImportMangaSelectFolder.tsx for the rationale).
//   - Inaccessible root folders render plain text (no link); Sonarr lets
//     the user click anyway and surfaces an error downstream. Phase 25.1
//     hides the link to prevent dead-end navigation per Mangarr UX
//     convention (matches the existing Settings/RootFolders row).
//
// Phase 8 cleanup: collapse with ImportSeriesSelectFolderRow when
// AddSeries/ deletes.
import React from 'react';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import { UnmappedFolder } from 'RootFolder/useRootFolders';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './ImportMangaSelectFolderRow.css';

interface ImportMangaSelectFolderRowProps {
  id: number;
  path: string;
  accessible: boolean;
  freeSpace?: number;
  unmappedFolders?: UnmappedFolder[];
}

function ImportMangaSelectFolderRow(props: ImportMangaSelectFolderRowProps) {
  const { id, path, accessible, freeSpace = 0, unmappedFolders = [] } = props;
  const isUnavailable = !accessible;
  const rowTestId = `import-manga-select-folder-row-${id}`;
  const linkTestId = `import-manga-select-folder-row-${id}-link`;

  return (
    <TableRow data-testid={rowTestId}>
      <TableRowCell>
        <div className={styles.pathContainer}>
          {isUnavailable ? (
            path
          ) : (
            <Link
              className={styles.link}
              to={`/add/import/${id}`}
              data-testid={linkTestId}
            >
              {path}
            </Link>
          )}

          {isUnavailable ? (
            <Label className={styles.label} kind={kinds.DANGER}>
              {translate('Unavailable')}
            </Label>
          ) : null}
        </div>
      </TableRowCell>

      <TableRowCell className={styles.freeSpace}>
        {isUnavailable || isNaN(Number(freeSpace))
          ? '-'
          : formatBytes(freeSpace)}
      </TableRowCell>

      <TableRowCell className={styles.unmappedFolders}>
        {isUnavailable ? '-' : unmappedFolders.length}
      </TableRowCell>
    </TableRow>
  );
}

export default ImportMangaSelectFolderRow;
