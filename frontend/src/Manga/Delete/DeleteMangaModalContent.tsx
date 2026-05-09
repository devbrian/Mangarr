// Sonarr divergence: NEW manga sibling per Phase 15 Plan 15-12 deferred
// "v1.1+ dedicated single-manga Delete modal" — referenced in
// frontend/src/Manga/Details/MangaDetails.tsx (Delete button toolbar wiring,
// previously a `{isDeleteModalOpen ? null : null}` stub).
// Role-match analog: frontend/src/Series/Delete/DeleteSeriesModal.tsx
// (deleted in Plan 15-07 and currently a `() => null` stub since Plan 15-12;
// NOT a useful structural reference — see additional_context note).
//
// Manga sibling preserves: ModalContent layout, FormGroup/FormInputGroup
// CHECK shell for the deleteFiles + addImportListExclusion checkboxes,
// SpinnerErrorButton submit, mangaOptionsStore-backed exclusion-flag
// persistence (mirrors the bulk Delete modal at
// frontend/src/Manga/Index/Select/Delete/DeleteMangaModalContent.tsx so the
// single-manga and bulk-select flows agree on which knobs persist across opens
// and which reset).
// Manga sibling diverges from the bulk DeleteMangaModalContent (per Phase 15
// + design philosophy "Preserve Sonarr's shape wherever it works"):
//   * Operates on one manga (mangaId prop) instead of a multi-select array
//     from useSelectedMangaStats. Reads via useSingleManga(mangaId).
//   * Wires useDeleteManga(mangaId, options) (singular DELETE
//     /api/v5/manga/{id}) instead of useBulkDeleteManga (DELETE
//     /api/v5/manga/editor with mangaIds).
//   * No MangaDeleteList list rendering — for a single manga, inlines the
//     path + size-on-disk readout in the modal body instead.
//   * Auto-closes on successful delete via usePrevious(isDeleting) +
//     useEffect — mirrors EditMangaModalContent.tsx's save-success effect.
//     Post-delete navigation back to /manga is handled organically by
//     MangaDetailsPage.tsx's redirect-on-vanish effect (the
//     useDeleteManga.onSuccess filters the manga out of the ['/manga']
//     React Query cache, the page-level effect detects the vanish and
//     history.pushes to /manga). The modal does NOT call history.push.
//
// Phase 8 cleanup: this file IS the manga canonical now (Series/Delete/
// stub will be removed when the Tv/ subtree deletes).
import React, { useCallback, useEffect, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { inputTypes, kinds } from 'Helpers/Props';
import Manga from 'Manga/Manga';
import {
  setMangaDeleteOptions,
  useMangaDeleteOptions,
} from 'Manga/mangaOptionsStore';
import { useDeleteManga, useSingleManga } from 'Manga/useManga';
import { InputChanged } from 'typings/inputs';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './DeleteMangaModalContent.css';

export interface DeleteMangaModalContentProps {
  mangaId: number;
  onModalClose: () => void;
}

interface DeleteMangaFormProps {
  manga: Manga;
  onModalClose: () => void;
}

function DeleteMangaForm({ manga, onModalClose }: DeleteMangaFormProps) {
  const { addImportListExclusion } = useMangaDeleteOptions();
  const [deleteFiles, setDeleteFiles] = useState(false);

  const { deleteManga, isDeleting, deleteError } = useDeleteManga(manga.id, {
    deleteFiles,
    addImportListExclusion,
  });
  const wasDeleting = usePrevious(isDeleting);

  const sizeOnDisk = manga.statistics?.sizeOnDisk ?? 0;
  const chapterFileCount = manga.statistics?.chapterFileCount ?? 0;

  const onDeleteFilesChange = useCallback(
    ({ value }: InputChanged<boolean>) => {
      setDeleteFiles(value);
    },
    [setDeleteFiles]
  );

  const onAddImportListExclusionChange = useCallback(
    ({ name, value }: { name: string; value: boolean }) => {
      setMangaDeleteOptions({
        [name]: value,
      });
    },
    []
  );

  const handleDeletePress = useCallback(() => {
    deleteManga();
  }, [deleteManga]);

  // Auto-close on successful delete — mirrors
  // EditMangaModalContent.tsx:127-132's save-success effect. Once the
  // useDeleteManga.onSuccess filters the manga out of the ['/manga'] cache,
  // MangaDetailsPage.tsx:34-44 will detect the vanish and redirect to /manga
  // organically; the modal close here just dismisses the dialog so that
  // redirect runs against an unblocked viewport.
  useEffect(() => {
    if (wasDeleting && !isDeleting && !deleteError) {
      onModalClose();
    }
  }, [isDeleting, wasDeleting, deleteError, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('DeleteSelectedSeries')} - {manga.title}
      </ModalHeader>

      <ModalBody>
        <div>
          <FormGroup>
            <FormLabel>{translate('Path')}</FormLabel>

            <div className={styles.pathContainer}>
              <span className={styles.path}>{manga.path}</span>
            </div>

            {chapterFileCount || sizeOnDisk ? (
              <div className={styles.statistics}>
                {chapterFileCount} {translate('Chapters')}
                {sizeOnDisk
                  ? `, ${translate('SizeOnDisk')}: ${formatBytes(sizeOnDisk)}`
                  : ''}
              </div>
            ) : null}
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('AddListExclusion')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="addImportListExclusion"
              value={addImportListExclusion}
              helpText={translate('AddListExclusionSeriesHelpText')}
              onChange={onAddImportListExclusionChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('DeleteMangaFolder')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="deleteFiles"
              value={deleteFiles}
              helpText={translate('DeleteMangaFolderHelpText')}
              kind="danger"
              onChange={onDeleteFilesChange}
            />
          </FormGroup>
        </div>

        {deleteFiles ? (
          <div className={styles.deleteFilesMessage}>
            {translate('DeleteMangaFolderCountWithFilesConfirmation', {
              count: 1,
            })}
          </div>
        ) : (
          <div className={styles.message}>
            {translate('DeleteMangaFolderCountConfirmation', { count: 1 })}
          </div>
        )}
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div className={styles.modalFooterButtons}>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <SpinnerErrorButton
            kind={kinds.DANGER}
            error={deleteError}
            isSpinning={isDeleting}
            onPress={handleDeletePress}
          >
            {translate('Delete')}
          </SpinnerErrorButton>
        </div>
      </ModalFooter>
    </ModalContent>
  );
}

function DeleteMangaModalContent({
  mangaId,
  onModalClose,
}: DeleteMangaModalContentProps) {
  const manga = useSingleManga(mangaId);

  if (!manga) {
    return null;
  }

  return <DeleteMangaForm manga={manga} onModalClose={onModalClose} />;
}

export default DeleteMangaModalContent;
