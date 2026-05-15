import React, { useCallback, useState } from 'react';
import Card from 'Components/Card';
import Label from 'Components/Label';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TagList from 'Components/TagList';
import { kinds } from 'Helpers/Props';
import { useTagList } from 'Tags/useTags';
import translate from 'Utilities/String/translate';
import { NotificationModel, useDeleteConnection } from '../useConnections';
import EditNotificationModal from './EditNotificationModal';
import styles from './Notification.css';

function Notification({
  id,
  name,
  onGrab,
  onDownload,
  onUpgrade,
  onImportComplete,
  onRename,
  onHealthIssue,
  onHealthRestored,
  onApplicationUpdate,
  onManualInteractionRequired,
  onChapterFileDelete,
  onChapterFileDeleteForUpgrade,
  onChapterImport,
  onMangaAdd,
  onMangaDelete,
  onMangaRename,
  supportsOnGrab,
  supportsOnDownload,
  supportsOnUpgrade,
  supportsOnImportComplete,
  supportsOnRename,
  supportsOnHealthIssue,
  supportsOnHealthRestored,
  supportsOnApplicationUpdate,
  supportsOnManualInteractionRequired,
  supportsOnChapterFileDelete,
  supportsOnChapterFileDeleteForUpgrade,
  supportsOnChapterImport,
  supportsOnMangaAdd,
  supportsOnMangaDelete,
  supportsOnMangaRename,
  tags,
}: NotificationModel) {
  const tagList = useTagList();
  const { deleteConnection } = useDeleteConnection(id);

  const [isEditNotificationModalOpen, setIsEditNotificationModalOpen] =
    useState(false);
  const [isDeleteNotificationModalOpen, setIsDeleteNotificationModalOpen] =
    useState(false);

  const handleEditNotificationPress = useCallback(() => {
    setIsEditNotificationModalOpen(true);
  }, []);

  const handleEditNotificationModalClose = useCallback(() => {
    setIsEditNotificationModalOpen(false);
  }, []);

  const handleDeleteNotificationPress = useCallback(() => {
    setIsEditNotificationModalOpen(false);
    setIsDeleteNotificationModalOpen(true);
  }, []);

  const handleDeleteNotificationModalClose = useCallback(() => {
    setIsDeleteNotificationModalOpen(false);
  }, []);

  const handleConfirmDeleteNotification = useCallback(() => {
    deleteConnection();
  }, [deleteConnection]);

  // gh157 fix-forward (mirrors PR #156 / live-indexer-card-click). Without
  // this annotation, `Page.GetByText("Komga"|"Kavita")` resolves to the inner
  // text <div> and the wrapping <button class="Card-underlay"> intercepts
  // pointer events (Card.tsx overlayContent shape). `settings-*` prefix is on
  // the allowed list (D-18 selector strategy, src/NzbDrone.Automation.Test/CLAUDE.md).
  // Slug = lowercased name with whitespace collapsed to dashes (matches
  // Indexer.tsx / MangaIndexPoster's titleSlug pattern).
  const testIdSlug = name.toLowerCase().replace(/\s+/g, '-');

  return (
    <Card
      className={styles.notification}
      overlayContent={true}
      data-testid={`settings-notification-card-${testIdSlug}`}
      onPress={handleEditNotificationPress}
    >
      <div className={styles.name}>{name}</div>

      {supportsOnGrab && onGrab ? (
        <Label kind={kinds.SUCCESS}>{translate('OnGrab')}</Label>
      ) : null}

      {supportsOnDownload && onDownload ? (
        <Label kind={kinds.SUCCESS}>{translate('OnFileImport')}</Label>
      ) : null}

      {supportsOnUpgrade && onDownload && onUpgrade ? (
        <Label kind={kinds.SUCCESS}>{translate('OnFileUpgrade')}</Label>
      ) : null}

      {supportsOnImportComplete && onImportComplete ? (
        <Label kind={kinds.SUCCESS}>{translate('OnImportComplete')}</Label>
      ) : null}

      {supportsOnRename && onRename ? (
        <Label kind={kinds.SUCCESS}>{translate('OnRename')}</Label>
      ) : null}

      {supportsOnHealthIssue && onHealthIssue ? (
        <Label kind={kinds.SUCCESS}>{translate('OnHealthIssue')}</Label>
      ) : null}

      {supportsOnHealthRestored && onHealthRestored ? (
        <Label kind={kinds.SUCCESS}>{translate('OnHealthRestored')}</Label>
      ) : null}

      {supportsOnApplicationUpdate && onApplicationUpdate ? (
        <Label kind={kinds.SUCCESS}>{translate('OnApplicationUpdate')}</Label>
      ) : null}

      {supportsOnManualInteractionRequired && onManualInteractionRequired ? (
        <Label kind={kinds.SUCCESS}>
          {translate('OnManualInteractionRequired')}
        </Label>
      ) : null}

      {supportsOnChapterFileDelete && onChapterFileDelete ? (
        <Label kind={kinds.SUCCESS}>{translate('OnChapterFileDelete')}</Label>
      ) : null}

      {supportsOnChapterFileDeleteForUpgrade &&
      onChapterFileDeleteForUpgrade ? (
        <Label kind={kinds.SUCCESS}>
          {translate('OnChapterFileDeleteForUpgrade')}
        </Label>
      ) : null}

      {supportsOnChapterImport && onChapterImport ? (
        <Label kind={kinds.SUCCESS}>{translate('OnChapterImport')}</Label>
      ) : null}

      {supportsOnMangaAdd && onMangaAdd ? (
        <Label kind={kinds.SUCCESS}>{translate('OnMangaAdd')}</Label>
      ) : null}

      {supportsOnMangaDelete && onMangaDelete ? (
        <Label kind={kinds.SUCCESS}>{translate('OnMangaDelete')}</Label>
      ) : null}

      {supportsOnMangaRename && onMangaRename ? (
        <Label kind={kinds.SUCCESS}>{translate('OnMangaRename')}</Label>
      ) : null}

      {!onGrab &&
      !onDownload &&
      !onRename &&
      !onImportComplete &&
      !onHealthIssue &&
      !onHealthRestored &&
      !onApplicationUpdate &&
      !onManualInteractionRequired &&
      !onChapterFileDelete &&
      !onChapterFileDeleteForUpgrade &&
      !onChapterImport &&
      !onMangaAdd &&
      !onMangaDelete &&
      !onMangaRename ? (
        <Label kind={kinds.DISABLED} outline={true}>
          {translate('Disabled')}
        </Label>
      ) : null}

      <TagList tags={tags} tagList={tagList} />

      <EditNotificationModal
        id={id}
        isOpen={isEditNotificationModalOpen}
        onModalClose={handleEditNotificationModalClose}
        onDeleteNotificationPress={handleDeleteNotificationPress}
      />

      <ConfirmModal
        isOpen={isDeleteNotificationModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteNotification')}
        message={translate('DeleteNotificationMessageText', { name })}
        confirmLabel={translate('Delete')}
        onConfirm={handleConfirmDeleteNotification}
        onCancel={handleDeleteNotificationModalClose}
      />
    </Card>
  );
}

export default Notification;
