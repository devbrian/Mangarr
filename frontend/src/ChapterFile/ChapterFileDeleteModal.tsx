// Mangarr divergence: NEW manga sibling — there is no `EpisodeFile/` delete-confirm
// modal on the Sonarr `v5-develop` reference (Sonarr deletes episode files from the
// season table's interactive-import / manage flow, not a per-file confirm). The manga
// Files tab (Manga Details > Files) gets a per-row delete affordance, so this confirm
// modal is authored fresh. Shape mirrors `Activity/Queue/RemoveQueueItemModal.tsx`
// (confirm message + a "Blocklist Release" checkbox + DANGER confirm button).
//
// Presentational only: the owning `ChapterFileRow` holds the `blocklist` checkbox state
// (so `useDeleteChapterFile(id, blocklist)` re-renders with the latest query param) and
// the delete mutation; this modal just renders + forwards events.
import React, { useCallback } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';

interface ChapterFileDeleteModalProps {
  isOpen: boolean;
  path: string;
  blocklist: boolean;
  isDeleting: boolean;
  onBlocklistChange: (value: boolean) => void;
  onDeletePress: () => void;
  onModalClose: () => void;
}

function ChapterFileDeleteModal(props: ChapterFileDeleteModalProps) {
  const {
    isOpen,
    path,
    blocklist,
    isDeleting,
    onBlocklistChange,
    onDeletePress,
    onModalClose,
  } = props;

  const handleBlocklistChange = useCallback(
    ({ value }: CheckInputChanged) => {
      onBlocklistChange(value);
    },
    [onBlocklistChange]
  );

  return (
    <Modal isOpen={isOpen} size={sizes.MEDIUM} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>{translate('DeleteChapterFile')}</ModalHeader>

        <ModalBody>
          <div>{translate('DeleteChapterFileMessageText', { path })}</div>

          <FormGroup>
            <FormLabel>{translate('BlocklistRelease')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="blocklist"
              value={blocklist}
              helpText={translate('BlocklistReleaseHelpText')}
              onChange={handleBlocklistChange}
            />
          </FormGroup>
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>{translate('Close')}</Button>

          <SpinnerButton
            kind={kinds.DANGER}
            isSpinning={isDeleting}
            onPress={onDeletePress}
          >
            {translate('Delete')}
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default ChapterFileDeleteModal;
