import React, { useCallback, useState } from 'react';
import Card from 'Components/Card';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TagList from 'Components/TagList';
import { icons, kinds } from 'Helpers/Props';
import { useTagList } from 'Tags/useTags';
import translate from 'Utilities/String/translate';
import {
  ImportListModel,
  useDeleteImportList,
} from '../useImportLists';
import EditImportListModal from './EditImportListModal';

// Phase 26 Plan 26-05 (IL-05) — single-row provider card. Mirror of
// frontend/src/Settings/Indexers/Indexers/Indexer.tsx with Indexer → ImportList
// field swap per RESEARCH §Q7. data-testid prefix `settings-importlist-*`
// (Phase 18 D-18 allowed prefix; sonarr-consistency-audit Pattern κ enforced).

interface ImportListProps extends ImportListModel {
  onCloneImportListPress: (id: number) => void;
}

function ImportList({
  id,
  name,
  enableAutomaticAdd,
  tags,
  onCloneImportListPress,
}: ImportListProps) {
  const tagList = useTagList();
  const { deleteImportList } = useDeleteImportList(id);

  const [isEditImportListModalOpen, setIsEditImportListModalOpen] = useState(false);
  const [isDeleteImportListModalOpen, setIsDeleteImportListModalOpen] =
    useState(false);

  const handleEditImportListPress = useCallback(() => {
    setIsEditImportListModalOpen(true);
  }, []);

  const handleEditImportListModalClose = useCallback(() => {
    setIsEditImportListModalOpen(false);
  }, []);

  const handleDeleteImportListPress = useCallback(() => {
    setIsEditImportListModalOpen(false);
    setIsDeleteImportListModalOpen(true);
  }, []);

  const handleDeleteImportListModalClose = useCallback(() => {
    setIsDeleteImportListModalOpen(false);
  }, []);

  const handleConfirmDeleteImportList = useCallback(() => {
    deleteImportList();
  }, [deleteImportList]);

  const handleCloneImportListPress = useCallback(() => {
    onCloneImportListPress(id);
  }, [id, onCloneImportListPress]);

  // testid slug derivation mirrors the Indexer.tsx pattern — lowercased + whitespace
  // collapsed. Phase 18 D-18 allowed prefix `settings-*`.
  const testIdSlug = name.toLowerCase().replace(/\s+/g, '-');

  return (
    <Card
      overlayContent={true}
      data-testid={`settings-importlist-card-${testIdSlug}`}
      onPress={handleEditImportListPress}
    >
      <div>
        <div>{name}</div>

        <IconButton
          title={translate('CloneImportList')}
          aria-label={translate('CloneImportList')}
          name={icons.CLONE}
          onPress={handleCloneImportListPress}
        />
      </div>

      <div>
        {enableAutomaticAdd ? (
          <Label kind={kinds.SUCCESS}>{translate('AutomaticAdd')}</Label>
        ) : (
          <Label kind={kinds.DISABLED} outline={true}>
            {translate('Disabled')}
          </Label>
        )}
      </div>

      <TagList tags={tags} tagList={tagList} />

      <EditImportListModal
        id={id}
        isOpen={isEditImportListModalOpen}
        onModalClose={handleEditImportListModalClose}
        onDeleteImportListPress={handleDeleteImportListPress}
      />

      <ConfirmModal
        isOpen={isDeleteImportListModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteImportList')}
        message={translate('DeleteImportListMessageText', { name })}
        confirmLabel={translate('Delete')}
        onConfirm={handleConfirmDeleteImportList}
        onCancel={handleDeleteImportListModalClose}
      />
    </Card>
  );
}

export default ImportList;
