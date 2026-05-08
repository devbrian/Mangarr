import React, { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import { fetchDownloadClientSchema } from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import AddDownloadClientItem from './AddDownloadClientItem';
import styles from './AddDownloadClientModalContent.css';

export interface AddDownloadClientModalContentProps {
  onDownloadClientSelect: () => void;
  onModalClose: () => void;
}

function AddDownloadClientModalContent({
  onDownloadClientSelect,
  onModalClose,
}: AddDownloadClientModalContentProps) {
  const dispatch = useDispatch();

  const { isSchemaFetching, isSchemaPopulated, schemaError, schema } =
    useSelector((state: AppState) => state.settings.downloadClients);

  useEffect(() => {
    dispatch(fetchDownloadClientSchema());
  }, [dispatch]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AddDownloadClient')}</ModalHeader>

      <ModalBody>
        {isSchemaFetching ? <LoadingIndicator /> : null}

        {!isSchemaFetching && !!schemaError ? (
          <Alert kind={kinds.DANGER}>
            {translate('AddDownloadClientError')}
          </Alert>
        ) : null}

        {isSchemaPopulated && !schemaError ? (
          <div>
            <Alert kind={kinds.INFO}>
              <div>{translate('SupportedDownloadClients')}</div>
              <div>{translate('SupportedDownloadClientsMoreInfo')}</div>
            </Alert>

            <FieldSet legend={translate('DownloadClients')}>
              <div className={styles.downloadClients}>
                {schema.map((downloadClient) => {
                  return (
                    <AddDownloadClientItem
                      key={downloadClient.implementation}
                      {...downloadClient}
                      implementation={downloadClient.implementation}
                      onDownloadClientSelect={onDownloadClientSelect}
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

export default AddDownloadClientModalContent;
