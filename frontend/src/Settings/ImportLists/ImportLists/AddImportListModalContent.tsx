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
import { useImportListSchema } from '../useImportLists';
import AddImportListItem from './AddImportListItem';

// Phase 26 Plan 26-05 (IL-05) — Add-picker modal. Mirror of
// frontend/src/Settings/Indexers/Indexers/AddIndexerModalContent.tsx per
// RESEARCH §Q7. Renders the V5 `/api/v5/importlist/schema` response as a
// single "Import Lists" FieldSet (no Sonarr-style listType grouping — Phase 26
// substrate ships zero providers per D-08 so the schema endpoint returns []
// and the FieldSet renders empty).
//
// Phase 27 will populate the picker once IMangaImportList implementations
// land (MangaDex / AniList / MyAnimeList). The single-FieldSet shape mirrors
// the in-tree Indexer fix-forward for Mangarr's flat http-only protocol
// shape (see Indexers/Indexers/AddIndexerModalContent.tsx Sonarr-divergence
// comment).

export interface AddImportListModalContentProps {
  onImportListSelect: (selectedSchema: SelectedSchema) => void;
  onModalClose: () => void;
}

function AddImportListModalContent({
  onImportListSelect,
  onModalClose,
}: AddImportListModalContentProps) {
  const { isSchemaFetching, isSchemaFetched, schemaError, schema } =
    useImportListSchema();

  return (
    <ModalContent data-testid="add-importlist-modal" onModalClose={onModalClose}>
      <ModalHeader>{translate('AddImportList')}</ModalHeader>

      <ModalBody>
        {isSchemaFetching ? <LoadingIndicator /> : null}

        {!isSchemaFetching && !!schemaError ? (
          <Alert kind={kinds.DANGER}>{translate('AddListError')}</Alert>
        ) : null}

        {isSchemaFetched && !schemaError ? (
          <div>
            <Alert kind={kinds.INFO}>
              <div>{translate('SupportedImportLists')}</div>
              <div>{translate('SupportedListsMoreInfo')}</div>
            </Alert>

            <FieldSet legend={translate('ImportLists')}>
              <div>
                {schema.map((importList) => {
                  return (
                    <AddImportListItem
                      key={importList.implementation}
                      {...importList}
                      implementation={importList.implementation}
                      onImportListSelect={onImportListSelect}
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

export default AddImportListModalContent;
