import React, { useCallback, useState } from 'react';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { icons } from 'Helpers/Props';
import { SelectedSchema } from 'Settings/useProviderSchema';
import translate from 'Utilities/String/translate';
import { useSortedImportLists } from '../useImportLists';
import AddImportListModal from './AddImportListModal';
import EditImportListModal from './EditImportListModal';
import ImportList from './ImportList';

// Phase 26 Plan 26-05 (IL-05) — provider-list view for the Settings → ImportLists
// page. Mirror of frontend/src/Settings/Indexers/Indexers/Indexers.tsx per
// RESEARCH §Q7. Empty list + empty Add picker is the default state for Phase 26
// (zero production providers per D-08; Phase 27 lands MangaDex / AniList / MAL
// IMangaImportList implementations).

function ImportLists() {
  const { isFetching, isFetched, data, error } = useSortedImportLists();

  const [isAddImportListModalOpen, setIsAddImportListModalOpen] =
    useState(false);
  const [isEditImportListModalOpen, setIsEditImportListModalOpen] =
    useState(false);
  const [cloneImportListId, setCloneImportListId] = useState<number | null>(
    null
  );

  const [selectedSchema, setSelectedSchema] = useState<
    SelectedSchema | undefined
  >(undefined);

  const handleAddImportListPress = useCallback(() => {
    setCloneImportListId(null);
    setIsAddImportListModalOpen(true);
  }, []);

  const handleCloneImportListPress = useCallback((id: number) => {
    setCloneImportListId(id);
    setIsEditImportListModalOpen(true);
  }, []);

  const handleImportListSelect = useCallback((selected: SelectedSchema) => {
    setSelectedSchema(selected);
    setIsAddImportListModalOpen(false);
    setIsEditImportListModalOpen(true);
  }, []);

  const handleAddImportListModalClose = useCallback(() => {
    setIsAddImportListModalOpen(false);
  }, []);

  const handleEditImportListModalClose = useCallback(() => {
    setCloneImportListId(null);
    setIsEditImportListModalOpen(false);
  }, []);

  return (
    <FieldSet legend={translate('ImportLists')}>
      <PageSectionContent
        errorMessage={translate('ImportListsLoadError')}
        error={error}
        isFetching={isFetching}
        isPopulated={isFetched}
      >
        <div>
          {data.map((item) => {
            return (
              <ImportList
                key={item.id}
                {...item}
                onCloneImportListPress={handleCloneImportListPress}
              />
            );
          })}

          <Card
            data-testid="settings-importlist-add-card"
            onPress={handleAddImportListPress}
          >
            <div>
              <Icon name={icons.ADD} size={45} />
            </div>
          </Card>
        </div>

        <AddImportListModal
          isOpen={isAddImportListModalOpen}
          onImportListSelect={handleImportListSelect}
          onModalClose={handleAddImportListModalClose}
        />

        <EditImportListModal
          isOpen={isEditImportListModalOpen}
          cloneId={cloneImportListId ?? undefined}
          selectedSchema={selectedSchema}
          onModalClose={handleEditImportListModalClose}
        />
      </PageSectionContent>
    </FieldSet>
  );
}

export default ImportLists;
