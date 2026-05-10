import React, { useCallback, useState } from 'react';
import Card from 'Components/Card';
import Label from 'Components/Label';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TagList from 'Components/TagList';
import { kinds } from 'Helpers/Props';
import { useTagList } from 'Tags/useTags';
import translate from 'Utilities/String/translate';
import {
  MetadataSourceModel,
  useDeleteMetadataSource,
} from '../useMetadataSources';
import EditMetadataSourceModal from './EditMetadataSourceModal';
import styles from './MetadataSource.css';

function MetadataSource({ id, name, isPrimary, tags }: MetadataSourceModel) {
  const tagList = useTagList();
  const { deleteMetadataSource } = useDeleteMetadataSource(id);

  const [isEditMetadataSourceModalOpen, setIsEditMetadataSourceModalOpen] =
    useState(false);
  const [isDeleteMetadataSourceModalOpen, setIsDeleteMetadataSourceModalOpen] =
    useState(false);

  const handleEditMetadataSourcePress = useCallback(() => {
    setIsEditMetadataSourceModalOpen(true);
  }, []);

  const handleEditMetadataSourceModalClose = useCallback(() => {
    setIsEditMetadataSourceModalOpen(false);
  }, []);

  const handleDeleteMetadataSourcePress = useCallback(() => {
    setIsEditMetadataSourceModalOpen(false);
    setIsDeleteMetadataSourceModalOpen(true);
  }, []);

  const handleDeleteMetadataSourceModalClose = useCallback(() => {
    setIsDeleteMetadataSourceModalOpen(false);
  }, []);

  const handleConfirmDeleteMetadataSource = useCallback(() => {
    deleteMetadataSource();
  }, [deleteMetadataSource]);

  return (
    <Card
      className={styles.metadataSource}
      overlayContent={true}
      onPress={handleEditMetadataSourcePress}
    >
      <div className={styles.name}>{name}</div>

      <div className={styles.labels}>
        {isPrimary ? (
          <Label kind={kinds.SUCCESS}>{translate('Primary')}</Label>
        ) : (
          <Label kind={kinds.DISABLED} outline={true}>
            {translate('Secondary')}
          </Label>
        )}
      </div>

      <TagList tags={tags} tagList={tagList} />

      <EditMetadataSourceModal
        id={id}
        isOpen={isEditMetadataSourceModalOpen}
        onModalClose={handleEditMetadataSourceModalClose}
        onDeleteMetadataSourcePress={handleDeleteMetadataSourcePress}
      />

      <ConfirmModal
        isOpen={isDeleteMetadataSourceModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteMetadataSource')}
        message={translate('DeleteMetadataSourceMessageText', { name })}
        confirmLabel={translate('Delete')}
        onConfirm={handleConfirmDeleteMetadataSource}
        onCancel={handleDeleteMetadataSourceModalClose}
      />
    </Card>
  );
}

export default MetadataSource;
