import React, { useCallback, useEffect, useMemo } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { OptionChanged } from 'Helpers/Hooks/useOptionsStore';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import {
  QueueOptions,
  setQueueOption,
  useQueueOption,
} from './queueOptionsStore';
import styles from './RemoveQueueItemModal.css';

interface RemoveQueueItemModalProps {
  isOpen: boolean;
  sourceTitle?: string;
  canChangeCategory: boolean;
  canIgnore: boolean;
  isPending: boolean;
  selectedCount?: number;
  onRemovePress(): void;
  onModalClose: () => void;
}

function RemoveQueueItemModal(props: RemoveQueueItemModalProps) {
  const {
    isOpen,
    sourceTitle = '',
    canIgnore,
    canChangeCategory,
    isPending,
    selectedCount,
    onRemovePress,
    onModalClose,
  } = props;

  const multipleSelected = selectedCount && selectedCount > 1;
  const { removeFromClient, blocklist, skipRedownload } =
    useQueueOption('removalOptions');

  // GH #308: when the item cannot be ignored or have its category changed it MUST leave the
  // download client, so the "Remove From Download Client" checkbox is locked checked (the same
  // mandatory-removal semantic the inherited SELECT dropdown enforced via isDisabled).
  const isRemoveLocked = !canChangeCategory && !canIgnore;
  const effectiveRemoveFromClient = isRemoveLocked ? true : removeFromClient;

  // GH #309 (Codex review): the DELETE query string is built from the PERSISTED store value
  // (useQueue.useRemovalOptions), NOT this component's `effectiveRemoveFromClient` display value.
  // If a user previously unchecked "Remove from Download Client" on a removable item, the store
  // holds `removeFromClient: false`; opening a later LOCKED (mandatory-removal) item would then
  // show the box checked-and-disabled but still submit `remove=false`, so the gateway job would
  // survive and the row reappear. Force the persisted value true while a locked modal is open so
  // the submitted query matches the locked UI.
  useEffect(() => {
    if (isOpen && isRemoveLocked && !removeFromClient) {
      setQueueOption('removalOptions', {
        removeFromClient: true,
        blocklist,
        skipRedownload,
      });
    }
  }, [isOpen, isRemoveLocked, removeFromClient, blocklist, skipRedownload]);

  const { title, message } = useMemo(() => {
    if (!selectedCount) {
      return {
        title: translate('RemoveQueueItem', { sourceTitle }),
        message: translate('RemoveQueueItemConfirmation', { sourceTitle }),
      };
    }

    if (selectedCount === 1) {
      return {
        title: translate('RemoveSelectedItem'),
        message: translate('RemoveSelectedItemQueueMessageText'),
      };
    }

    return {
      title: translate('RemoveSelectedItems'),
      message: translate('RemoveSelectedItemsQueueMessageText', {
        selectedCount,
      }),
    };
  }, [sourceTitle, selectedCount]);

  const handleRemovalOptionInputChange = useCallback(
    ({ name, value }: OptionChanged<QueueOptions['removalOptions']>) => {
      setQueueOption('removalOptions', {
        removeFromClient,
        blocklist,
        skipRedownload,
        [name]: value,
      });
    },
    [removeFromClient, blocklist, skipRedownload]
  );

  const handleConfirmRemove = useCallback(() => {
    onRemovePress();
  }, [onRemovePress]);

  const handleModalClose = useCallback(() => {
    onModalClose();
  }, [onModalClose]);

  return (
    <Modal isOpen={isOpen} size={sizes.MEDIUM} onModalClose={handleModalClose}>
      <ModalContent onModalClose={handleModalClose}>
        <ModalHeader>{title}</ModalHeader>

        <ModalBody>
          <div className={styles.message}>{message}</div>

          {isPending ? null : (
            <FormGroup>
              <FormLabel>{translate('RemoveFromDownloadClient')}</FormLabel>

              <FormInputGroup
                type={inputTypes.CHECK}
                name="removeFromClient"
                value={effectiveRemoveFromClient}
                isDisabled={isRemoveLocked}
                helpText={
                  multipleSelected
                    ? translate('RemoveMultipleFromDownloadClientHint')
                    : translate('RemoveFromDownloadClientHint')
                }
                helpTextWarning={
                  isRemoveLocked
                    ? translate('RemoveQueueItemRemovalMethodHelpTextWarning')
                    : undefined
                }
                // @ts-expect-error - The typing for inputs needs more work
                onChange={handleRemovalOptionInputChange}
              />
            </FormGroup>
          )}

          <FormGroup>
            <FormLabel>
              {multipleSelected
                ? translate('BlocklistReleases')
                : translate('BlocklistRelease')}
            </FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="blocklist"
              value={blocklist}
              helpText={translate('BlocklistReleaseHelpText')}
              // @ts-expect-error - The typing for inputs needs more work
              onChange={handleRemovalOptionInputChange}
            />
          </FormGroup>

          {blocklist ? (
            <FormGroup>
              <FormLabel>{translate('SkipRedownload')}</FormLabel>

              <FormInputGroup
                type={inputTypes.CHECK}
                name="skipRedownload"
                value={skipRedownload}
                isDisabled={isPending}
                helpText={translate('SkipRedownloadHelpText')}
                // @ts-expect-error - The typing for inputs needs more work
                onChange={handleRemovalOptionInputChange}
              />
            </FormGroup>
          ) : null}
        </ModalBody>

        <ModalFooter>
          <Button onPress={handleModalClose}>{translate('Close')}</Button>

          <Button kind={kinds.DANGER} onPress={handleConfirmRemove}>
            {translate('Remove')}
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default RemoveQueueItemModal;
