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

// Phase 17 follow-up (debug qualityprofiles-redux-rename, 2026-05-12 — GH #82
// Path 1 surface-rename cascade): the inherited bulk-edit `SavePayload`
// originally carried `qualityProfileId` + `seriesType` + `seasonFolder`. The
// Manga editor backend (`Mangarr.Api.V5/Manga/MangaEditorResource.cs`) accepts
// `TranslationProfileId` instead of `QualityProfileId` (Phase 5 D-05 split);
// the bulk modal now POSTs `translationProfileId`. `seriesType` +
// `seasonFolder` were dropped earlier in Plan 17.3-11 (D-14 / D-13 cascade —
// manga has no series-type axis and no season-folder concept per DOMAIN-02);
// they are scrubbed from this local typedef in this commit for parity.
interface SavePayload {
  monitored?: boolean;
  translationProfileId?: number;
  rootFolderPath?: string;
  moveFiles?: boolean;
}

function MangaIndexSelectFooter() {
  const { saveMangaEditor, isSavingMangaEditor } = useSaveMangaEditor();
  // WR-11: updateMangaMonitor is destructured but unused at v1 — the bulk
  // monitor button is gated DISABLED (see SpinnerButton below). The hook
  // is still invoked so Phase 8 cutover can re-enable the button without
  // re-introducing the hook call. Underscore-prefix marks intentional disuse.
  const { updateMangaMonitor: _updateMangaMonitor, isUpdatingMangaMonitor } =
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
  // WR-11: bulk-monitor flow is not wired in v1 (Plan 07-04 Lock #10 dropped
  // ChangeMonitoringModal — manga has no seasons). The "Update Monitoring"
  // button is gated DISABLED below until a future plan lands the manga
  // bulk-monitor surface. The dead modal-open state, close handler, and
  // save handler that previously held the line via `void` casts have been
  // removed; the per-manga monitor toggle on each row remains functional.
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isDeleteFilesModalOpen, setIsDeleteFilesModalOpen] = useState(false);
  const [isSavingSeries, setIsSavingSeries] = useState(false);
  const [isSavingTags, setIsSavingTags] = useState(false);
  const [isSavingMonitoring] = useState(false);
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

  // WR-11: onMonitoringPress / onMonitoringClose / onMonitoringSavePress
  // removed alongside the dead modal-open state above. The "Update
  // Monitoring" button now no-ops via an undefined onPress + isDisabled
  // gate; future bulk-monitor plan will re-introduce a real handler.

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
      // WR-11: setIsSavingMonitoring removed — no setter exists now that
      // the bulk-monitor flow is gated off. The state is permanently false
      // and the spinner never spins on the disabled button.
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

          {/*
            WR-11: bulk Update Monitoring is gated DISABLED in v1 — the
            ChangeMonitoringModal subtree was dropped per Plan 07-04 Lock
            #10 and the manga bulk-monitor flow has not been wired yet.
            The button stays in place for layout parity with the Series
            sibling so the Phase 8 cutover can re-enable it without
            re-introducing a button. onPress is intentionally undefined.
           */}
          <SpinnerButton
            isSpinning={isSaving && isSavingMonitoring}
            isDisabled={true}
            onPress={undefined}
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
        {/* WR-11: was translate('CountSeriesSelected') — TV-specific key
            on the manga footer. Swapped to manga-specific key. */}
        {translate('CountMangaSelected', { count: selectedCount })}
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
