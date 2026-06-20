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
// Renders cells in the order declared by `MangaDetailsFiles.COLUMNS`:
//   * relativePath / path (fallback)
//   * size (formatBytes)
//   * translatedLanguage (Chapter/LanguageBadge — accent flip when the chapter
//     language matches the user's #1-ranked language on the default
//     Translation Profile; falls back to nothing when undefined)
//   * scanlationGroup (plain string)
//   * dateAdded (RelativeDateCell)
//   * actions — delete button opening ChapterFileDeleteModal (with an optional
//     "Blocklist Release" checkbox). Mangarr-only affordance; the EpisodeFile
//     peer has no per-row delete. The row owns the delete mutation + the
//     blocklist checkbox state so `useDeleteChapterFile(id, blocklist)` re-renders
//     with the latest `?blocklist=` query param on every toggle.
//
// Phase 8 cleanup: N/A — manga-specific row composition.
import React, { useCallback, useState } from 'react';
import LanguageBadge from 'Chapter/LanguageBadge';
import IconButton from 'Components/Link/IconButton';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { icons } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import ChapterFile from './ChapterFile';
import ChapterFileDeleteModal from './ChapterFileDeleteModal';
import { useDeleteChapterFile } from './useChapterFile';
import styles from './ChapterFileRow.css';

interface ChapterFileRowProps {
  file: ChapterFile;
}

function ChapterFileRow({ file }: ChapterFileRowProps) {
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [blocklist, setBlocklist] = useState(false);

  const { deleteChapterFile, isDeleting } = useDeleteChapterFile(
    file.id,
    blocklist
  );

  const handleDeletePress = useCallback(() => {
    setIsDeleteModalOpen(true);
  }, []);

  const handleModalClose = useCallback(() => {
    setIsDeleteModalOpen(false);
    setBlocklist(false);
  }, []);

  const handleConfirmDelete = useCallback(() => {
    deleteChapterFile(undefined, {
      onSuccess: () => {
        setIsDeleteModalOpen(false);
        setBlocklist(false);
      },
    });
  }, [deleteChapterFile]);

  return (
    <TableRow>
      <TableRowCell>{file.relativePath ?? file.path ?? ''}</TableRowCell>
      <TableRowCell>{formatBytes(file.size)}</TableRowCell>
      <TableRowCell>
        <LanguageBadge language={file.translatedLanguage} />
      </TableRowCell>
      <TableRowCell>{file.scanlationGroup ?? ''}</TableRowCell>
      <RelativeDateCell date={file.dateAdded} />

      <TableRowCell className={styles.actions}>
        <IconButton
          title={translate('DeleteChapterFile')}
          aria-label={translate('DeleteChapterFile')}
          name={icons.DELETE}
          data-testid={`chapter-file-row-${file.id}-delete-button`}
          onPress={handleDeletePress}
        />
      </TableRowCell>

      <ChapterFileDeleteModal
        isOpen={isDeleteModalOpen}
        path={file.relativePath ?? file.path ?? ''}
        blocklist={blocklist}
        isDeleting={isDeleting}
        onBlocklistChange={setBlocklist}
        onDeletePress={handleConfirmDelete}
        onModalClose={handleModalClose}
      />
    </TableRow>
  );
}

export default ChapterFileRow;
