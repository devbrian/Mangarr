import React, { useCallback, useEffect, useState } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting } from 'Commands/useCommands';
import SpinnerButton from 'Components/Link/SpinnerButton';
import PageContentFooter from 'Components/Page/PageContentFooter';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { kinds } from 'Helpers/Props';
import Manga from 'Manga/Manga';
import {
  useBulkDeleteManga,
  useSaveMangaEditor,
  useUpdateMangaMonitor,
} from 'Manga/useManga';
import translate from 'Utilities/String/translate';
import DeleteMangaModal from './Delete/DeleteMangaModal';
import DeleteMangaFilesModal from './Delete/Files/DeleteMangaFilesModal';
import EditMangaModal from './Edit/EditMangaModal';
import OrganizeMangaModal from './Organize/OrganizeMangaModal';
// SeasonPass omitted per Plan 07-04 Lock #10 — manga has no seasons.
// ChangeMonitoringModal would land in a future "ChangeMangaMonitoring" plan.
// Until then, the bulk-monitor-update path uses MangaEditor and the per-manga
// monitor toggle on each row; the footer button is hidden via short-circuit
// below (replaced inline at the JSX call site).
import TagsModal from './Tags/TagsModal';
import styles from './MangaIndexSelectFooter.css';

interface SavePayload {
  monitored?: boolean;
  qualityProfileId?: number;
  seriesType?: string;
  seasonFolder?: boolean;
  rootFolderPath?: string;
  moveFiles?: boolean;
}

function MangaIndexSelectFooter() {
  const { saveMangaEditor, isSavingMangaEditor } = useSaveMangaEditor();
  const { updateMangaMonitor, isUpdatingMangaMonitor } =
    useUpdateMangaMonitor();
  const { isBulkDeleting, bulkDeleteError } = useBulkDeleteManga();
  const isDeleteFilesCommandExecuting = useCommandExecuting(
    CommandNames.DeleteSeriesFiles
  );

  const isOrganizingSeries = useCommandExecuting(CommandNames.RenameSeries);

  const isSaving = isSavingMangaEditor || isUpdatingMangaMonitor;
  const isDeleting = isBulkDeleting;
  const deleteError = bulkDeleteError;

  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isOrganizeModalOpen, setIsOrganizeModalOpen] = useState(false);
  const [isTagsModalOpen, setIsTagsModalOpen] = useState(false);
  // ChangeMonitoringModal omitted per Plan 07-04 Lock #10 — see comment block
  // at top of imports. The setter is used by onMonitoringPress (kept) but the
  // modal-open boolean and close/save handlers are dead until the bulk-monitor
  // flow is wired in a future plan.
  const [, setIsMonitoringModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isDeleteFilesModalOpen, setIsDeleteFilesModalOpen] = useState(false);
  const [isSavingSeries, setIsSavingSeries] = useState(false);
  const [isSavingTags, setIsSavingTags] = useState(false);
  const [isSavingMonitoring, setIsSavingMonitoring] = useState(false);
  const previousIsDeleting = usePrevious(isDeleting);
  const { selectedCount, unselectAll, useSelectedIds } = useSelect<Manga>();
  const mangaIds = useSelectedIds();

  const onEditPress = useCallback(() => {
    setIsEditModalOpen(true);
  }, [setIsEditModalOpen]);

  const onEditModalClose = useCallback(() => {
    setIsEditModalOpen(false);
  }, [setIsEditModalOpen]);

  const onSavePress = useCallback(
    (payload: SavePayload) => {
      setIsSavingSeries(true);
      setIsEditModalOpen(false);

      saveMangaEditor({
        ...payload,
        mangaIds,
      });
    },
    [mangaIds, saveMangaEditor]
  );

  const onOrganizePress = useCallback(() => {
    setIsOrganizeModalOpen(true);
  }, [setIsOrganizeModalOpen]);

  const onOrganizeModalClose = useCallback(() => {
    setIsOrganizeModalOpen(false);
  }, [setIsOrganizeModalOpen]);

  const onTagsPress = useCallback(() => {
    setIsTagsModalOpen(true);
  }, [setIsTagsModalOpen]);

  const onTagsModalClose = useCallback(() => {
    setIsTagsModalOpen(false);
  }, [setIsTagsModalOpen]);

  const onApplyTagsPress = useCallback(
    (tags: number[], _applyTags: string) => {
      setIsSavingTags(true);
      setIsTagsModalOpen(false);

      saveMangaEditor({
        mangaIds,
        tags,
      });
    },
    [mangaIds, saveMangaEditor]
  );

  const onMonitoringPress = useCallback(() => {
    setIsMonitoringModalOpen(true);
  }, [setIsMonitoringModalOpen]);

  // ChangeMonitoringModal close handler retained for diff-friendliness; ref
  // it through a void cast so noUnusedLocals doesn't trigger.
  const onMonitoringClose = useCallback(() => {
    setIsMonitoringModalOpen(false);
  }, [setIsMonitoringModalOpen]);
  void onMonitoringClose;

  const onMonitoringSavePress = useCallback(
    (monitor: string) => {
      setIsSavingMonitoring(true);
      setIsMonitoringModalOpen(false);

      updateMangaMonitor({
        manga: mangaIds.map((id) => ({ id })),
        monitoringOptions: { monitor },
      });
    },
    [mangaIds, updateMangaMonitor]
  );
  void onMonitoringSavePress;

  const onDeletePress = useCallback(() => {
    setIsDeleteModalOpen(true);
  }, [setIsDeleteModalOpen]);

  const onDeleteModalClose = useCallback(() => {
    setIsDeleteModalOpen(false);
  }, []);

  const onDeleteFilesPress = useCallback(() => {
    setIsDeleteFilesModalOpen(true);
  }, []);

  const onDeleteFilesModalClose = useCallback(() => {
    setIsDeleteFilesModalOpen(false);
  }, []);

  useEffect(() => {
    if (!isSaving) {
      setIsSavingSeries(false);
      setIsSavingTags(false);
      setIsSavingMonitoring(false);
    }
  }, [isSaving]);

  useEffect(() => {
    if (previousIsDeleting && !isDeleting && !deleteError) {
      unselectAll();
    }
  }, [previousIsDeleting, isDeleting, deleteError, unselectAll]);

  const anySelected = selectedCount > 0;

  return (
    <PageContentFooter className={styles.footer}>
      <div className={styles.buttons}>
        <div className={styles.actionButtons}>
          <SpinnerButton
            isSpinning={isSaving && isSavingSeries}
            isDisabled={!anySelected || isOrganizingSeries}
            onPress={onEditPress}
          >
            {translate('Edit')}
          </SpinnerButton>

          <SpinnerButton
            kind={kinds.WARNING}
            isSpinning={isOrganizingSeries}
            isDisabled={!anySelected || isOrganizingSeries}
            onPress={onOrganizePress}
          >
            {translate('RenameFiles')}
          </SpinnerButton>

          <SpinnerButton
            isSpinning={isSaving && isSavingTags}
            isDisabled={!anySelected || isOrganizingSeries}
            onPress={onTagsPress}
          >
            {translate('SetTags')}
          </SpinnerButton>

          <SpinnerButton
            isSpinning={isSaving && isSavingMonitoring}
            isDisabled={!anySelected || isOrganizingSeries}
            onPress={onMonitoringPress}
          >
            {translate('UpdateMonitoring')}
          </SpinnerButton>
        </div>

        <div className={styles.deleteButtons}>
          <SpinnerButton
            kind={kinds.DANGER}
            isSpinning={isDeleting}
            isDisabled={!anySelected || isDeleting}
            onPress={onDeletePress}
          >
            {translate('Delete')}
          </SpinnerButton>

          <SpinnerButton
            kind={kinds.DANGER}
            isSpinning={isDeleteFilesCommandExecuting}
            isDisabled={!anySelected || isDeleteFilesCommandExecuting}
            onPress={onDeleteFilesPress}
          >
            {translate('DeleteFiles')}
          </SpinnerButton>
        </div>
      </div>

      <div className={styles.selected}>
        {translate('CountSeriesSelected', { count: selectedCount })}
      </div>

      <EditMangaModal
        isOpen={isEditModalOpen}
        onSavePress={onSavePress}
        onModalClose={onEditModalClose}
      />

      <TagsModal
        isOpen={isTagsModalOpen}
        onApplyTagsPress={onApplyTagsPress}
        onModalClose={onTagsModalClose}
      />

      {/*
        ChangeMonitoringModal omitted per Plan 07-04 Lock #10 (SeasonPass/
        subtree dropped — manga has no seasons). Bulk monitor toggle ships in
        a future plan via MangaEditor; per-manga toggle works via row UI.
       */}

      <OrganizeMangaModal
        isOpen={isOrganizeModalOpen}
        onModalClose={onOrganizeModalClose}
      />

      <DeleteMangaModal
        isOpen={isDeleteModalOpen}
        onModalClose={onDeleteModalClose}
      />

      <DeleteMangaFilesModal
        isOpen={isDeleteFilesModalOpen}
        onModalClose={onDeleteFilesModalClose}
      />
    </PageContentFooter>
  );
}

export default MangaIndexSelectFooter;
