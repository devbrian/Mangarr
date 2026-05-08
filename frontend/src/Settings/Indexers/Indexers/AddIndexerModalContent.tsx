import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import { SelectedSchema } from 'Settings/useProviderSchema';
import translate from 'Utilities/String/translate';
import { useIndexerSchema } from '../useIndexers';
import AddIndexerItem from './AddIndexerItem';
import styles from './AddIndexerModalContent.css';

export interface AddIndexerModalContentProps {
  onIndexerSelect: (selectedSchema: SelectedSchema) => void;
  onModalClose: () => void;
}

function AddIndexerModalContent({
  onIndexerSelect,
  onModalClose,
}: AddIndexerModalContentProps) {
  const { isSchemaFetching, isSchemaFetched, schemaError, schema } =
    useIndexerSchema();

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AddIndexer')}</ModalHeader>

      <ModalBody>
        {isSchemaFetching ? <LoadingIndicator /> : null}

        {!isSchemaFetching && !!schemaError ? (
          <Alert kind={kinds.DANGER}>{translate('AddIndexerError')}</Alert>
        ) : null}

        {isSchemaFetched && !schemaError ? (
          <div>
            <Alert kind={kinds.INFO}>
              <div>{translate('SupportedIndexers')}</div>
              <div>{translate('SupportedIndexersMoreInfo')}</div>
            </Alert>

            {/*
              Sonarr divergence: Phase 15 D-18 reduced DownloadProtocol to {Unknown, Http}
              (TV usenet/torrent variants deleted). Manga aggregator indexers (MangaDex,
              Comix, future MangaFire) all emit `protocol === 'http'`. The original Sonarr
              UI bucketed schema items into Usenet/Torrent FieldSets, which silently dropped
              anything that wasn't one of those two — leaving the Add-Indexer modal empty
              for manga sources. This is the indexer twin of the prior DownloadClients fix
              (.planning/debug/resolved/downloadclients-load-fail.md): render the schema as
              a single "Indexers" group.
            */}
            <FieldSet legend={translate('Indexers')}>
              <div className={styles.indexers}>
                {schema.map((indexer) => {
                  return (
                    <AddIndexerItem
                      key={indexer.implementation}
                      {...indexer}
                      implementation={indexer.implementation}
                      onIndexerSelect={onIndexerSelect}
                    />
                  );
                })}
              </div>
            </FieldSet>
          </div>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddIndexerModalContent;
