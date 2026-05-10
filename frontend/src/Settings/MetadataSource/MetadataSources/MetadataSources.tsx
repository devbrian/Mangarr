import React, { useCallback, useState } from 'react';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { icons } from 'Helpers/Props';
import { SelectedSchema } from 'Settings/useProviderSchema';
import translate from 'Utilities/String/translate';
import {
  useMetadataSources,
  useSortedMetadataSources,
} from '../useMetadataSources';
import AddMetadataSourceModal from './AddMetadataSourceModal';
import EditMetadataSourceModal from './EditMetadataSourceModal';
import MetadataSource from './MetadataSource';
import styles from './MetadataSources.css';

function MetadataSources() {
  const { error, isFetching, isFetched } = useMetadataSources();
  const items = useSortedMetadataSources();

  const [selectedSchema, setSelectedSchema] = useState<
    SelectedSchema | undefined
  >(undefined);

  const [isAddMetadataSourceModalOpen, setIsAddMetadataSourceModalOpen] =
    useState(false);

  const [isEditMetadataSourceModalOpen, setIsEditMetadataSourceModalOpen] =
    useState(false);

  const handleAddMetadataSourcePress = useCallback(() => {
    setIsAddMetadataSourceModalOpen(true);
  }, []);

  const handleMetadataSourceSelect = useCallback((selected: SelectedSchema) => {
    setSelectedSchema(selected);
    setIsAddMetadataSourceModalOpen(false);
    setIsEditMetadataSourceModalOpen(true);
  }, []);

  const handleAddMetadataSourceModalClose = useCallback(() => {
    setIsAddMetadataSourceModalOpen(false);
  }, []);

  const handleEditMetadataSourceModalClose = useCallback(() => {
    setIsEditMetadataSourceModalOpen(false);
    setSelectedSchema(undefined);
  }, []);

  return (
    <FieldSet legend={translate('MetadataSources')}>
      <PageSectionContent
        errorMessage={translate('MetadataSourcesLoadError')}
        error={error}
        isFetching={isFetching}
        isPopulated={isFetched}
      >
        <div className={styles.metadataSources}>
          {items.map((item) => (
            <MetadataSource key={item.id} {...item} />
          ))}

          <Card
            className={styles.addMetadataSource}
            onPress={handleAddMetadataSourcePress}
          >
            <div className={styles.center}>
              <Icon name={icons.ADD} size={45} />
            </div>
          </Card>
        </div>

        <AddMetadataSourceModal
          isOpen={isAddMetadataSourceModalOpen}
          onMetadataSourceSelect={handleMetadataSourceSelect}
          onModalClose={handleAddMetadataSourceModalClose}
        />

        <EditMetadataSourceModal
          isOpen={isEditMetadataSourceModalOpen}
          selectedSchema={selectedSchema}
          onModalClose={handleEditMetadataSourceModalClose}
        />
      </PageSectionContent>
    </FieldSet>
  );
}

export default MetadataSources;
