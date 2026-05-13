// Sonarr divergence: NEW manga sibling per issue #84 (Plan 17.3-16 deferral
// resolution — Option A). See DIVERGENCE.md.
//
// Role-match analog: there is no single-row presentational peer in
// frontend/src/EpisodeFile/ on the Sonarr `v5-develop` reference — Sonarr's
// per-series Files panel doesn't ship a dedicated row component. The manga
// sibling adds this thin wrapper to keep `MangaDetailsFiles.tsx` focused on
// table-shell concerns (sort, fetch, error/empty/loading states) while the
// per-row cell composition lives next to the ChapterFile type that drives it.
//
// Renders four cells in the order declared by `MangaDetailsFiles.COLUMNS`:
//   * relativePath / path (fallback)
//   * size (formatBytes)
//   * translatedLanguage (Chapter/LanguageBadge — accent flip when the chapter
//     language matches the user's #1-ranked language on the default
//     Translation Profile; falls back to nothing when undefined)
//   * scanlationGroup (plain string)
//   * dateAdded (RelativeDateCell)
//
// Phase 8 cleanup: N/A — manga-specific row composition.
import React from 'react';
import LanguageBadge from 'Chapter/LanguageBadge';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import formatBytes from 'Utilities/Number/formatBytes';
import ChapterFile from './ChapterFile';

interface ChapterFileRowProps {
  file: ChapterFile;
}

function ChapterFileRow({ file }: ChapterFileRowProps) {
  return (
    <TableRow>
      <TableRowCell>{file.relativePath ?? file.path ?? ''}</TableRowCell>
      <TableRowCell>{formatBytes(file.size)}</TableRowCell>
      <TableRowCell>
        <LanguageBadge language={file.translatedLanguage} />
      </TableRowCell>
      <TableRowCell>{file.scanlationGroup ?? ''}</TableRowCell>
      <RelativeDateCell date={file.dateAdded} />
    </TableRow>
  );
}

export default ChapterFileRow;
