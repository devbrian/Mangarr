import React from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import { SelectedSchema } from 'Settings/useProviderSchema';
import translate from 'Utilities/String/translate';
import { useMetadataSourceSchema } from '../useMetadataSources';
import AddMetadataSourceItem from './AddMetadataSourceItem';
import styles from './AddMetadataSourceModalContent.css';

export interface AddMetadataSourceModalContentProps {
  onMetadataSourceSelect: (selectedSchema: SelectedSchema) => void;
  onModalClose: () => void;
}

function AddMetadataSourceModalContent({
  onMetadataSourceSelect,
  onModalClose,
}: AddMetadataSourceModalContentProps) {
  const { isSchemaFetching, isSchemaFetched, schemaError, schema } =
    useMetadataSourceSchema();

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AddMetadataSource')}</ModalHeader>

      <ModalBody>
        {isSchemaFetching && !isSchemaFetched ? <LoadingIndicator /> : null}

        {!isSchemaFetching && !!schemaError ? (
          <Alert kind={kinds.DANGER}>
            {translate('AddMetadataSourceError')}
          </Alert>
        ) : null}

        {isSchemaFetched && !schemaError ? (
          <div className={styles.metadataSources}>
            {schema.map((source) => {
              return (
                <AddMetadataSourceItem
                  key={source.implementation}
                  {...source}
                  implementation={source.implementation}
                  onMetadataSourceSelect={onMetadataSourceSelect}
                />
              );
            })}
          </div>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddMetadataSourceModalContent;
