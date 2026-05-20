import React from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Modal from 'Components/Modal/Modal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { useImportListsData } from '../../useImportLists';

// Phase 26 Plan 26-05 (IL-05) — bulk-manage modal shell. Mirror of
// frontend/src/Settings/Indexers/Indexers/Manage/ManageIndexersModal.tsx per
// RESEARCH §Q7. Substrate-only: this modal ships in Phase 26 as a thin shell
// because the Sonarr-canonical ManageIndexers component pulls in 7+ supporting
// files (Row + Edit/ + Tags/ + sort store + SelectProvider) that would breach
// Plan 26-05's 23-file budget (D-06 LOCKED — files_modified enumerates only
// `Manage/ManageImportListsModal.tsx`).
//
// Phase 27 substrate-completion plan will expand this into the full
// table + bulk-edit + bulk-tags-apply flow once concrete IMangaImportList
// providers ship and the bulk surface has live data to manipulate. The empty
// state shipped here renders cleanly in Phase 26's smoke gate (D-08: zero
// providers means there are no rows to manage anyway).

interface ManageImportListsModalProps {
  isOpen: boolean;
  onModalClose: () => void;
}

function ManageImportListsModal({
  isOpen,
  onModalClose,
}: ManageImportListsModalProps) {
  const data = useImportListsData();

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent
        data-testid="manage-importlists-modal"
        onModalClose={onModalClose}
      >
        <ModalHeader>{translate('ManageImportLists')}</ModalHeader>

        <ModalBody>
          {data.length === 0 ? (
            <Alert kind={kinds.INFO}>
              {translate('NoImportListsFound')}
            </Alert>
          ) : (
            <div data-testid="manage-importlists-list">
              {data.map((item) => (
                <div key={item.id}>{item.name}</div>
              ))}
            </div>
          )}
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default ManageImportListsModal;
