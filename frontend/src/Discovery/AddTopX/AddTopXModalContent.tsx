// Phase 42 Plan 42-07 — count-only bulk-add modal body (sketch 002, LOCKED).
//
// Mirrors the single-add AddNewMangaModalContent shell but count-only: the body
// reuses the SAME factored <AddMangaFormBody> fed a one-line count summary
// ("N manga will be added") instead of the poster/overview, and reads the SAME
// addMangaOptionsStore so the defaults match single-add. On submit it POSTs the
// grid's MangaBakaIds to /discovery/bulk-add via useDiscoveryBulkAdd and closes
// IMMEDIATELY (fire-and-forget, D-07) — it does NOT await the 202; progress
// rides the existing command-queue/SignalR fan-out the FE already watches. The
// added rows then drop from the grid optimistically (D-08) via onAdded.
import React, { useCallback } from 'react';
import {
  setAddMangaOption,
  useAddMangaOptions,
} from 'AddManga/addMangaOptionsStore';
import AddMangaFormBody from 'AddManga/AddNewManga/AddMangaFormBody';
import CheckInput from 'Components/Form/CheckInput';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { useDiscoveryBulkAdd } from 'Discovery/useDiscovery';
import { kinds } from 'Helpers/Props';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './AddTopXModalContent.css';

export interface AddTopXModalContentProps {
  mangaBakaIds: number[];
  onAdded: (mangaBakaIds: number[]) => void;
  onModalClose: () => void;
}

function AddTopXModalContent({
  mangaBakaIds,
  onAdded,
  onModalClose,
}: AddTopXModalContentProps) {
  const options = useAddMangaOptions();
  const { mutate: bulkAdd, error: addError } = useDiscoveryBulkAdd();

  const count = mangaBakaIds.length;

  // Same root-folder submit gate as single-add (pr-smoke-add-manga-timeout):
  // never POST while the root folder is still unpopulated.
  const isRootFolderMissing = !options.rootFolderPath;

  const handleSearchToggleChange = useCallback(
    ({ value }: CheckInputChanged) => {
      setAddMangaOption('searchForMissingChapters', value);
    },
    []
  );

  const handleAddPress = useCallback(() => {
    if (isRootFolderMissing || count === 0) {
      return;
    }

    // Fire-and-forget (D-07): kick the bulk-add mutation but do NOT await it.
    // The backend enqueues DiscoveryBulkAddCommand + returns 202; library
    // updates arrive via the existing `manga` SignalR fan-out + command queue.
    bulkAdd({
      mangaBakaIds,
      rootFolderPath: options.rootFolderPath,
      monitor: options.monitor,
      translationProfileId: options.translationProfileId,
      customFormatProfileId: options.customFormatProfileId,
      tags: options.tags,
      searchForMissingChapters: options.searchForMissingChapters,
    });

    // Drop the added rows from the grid optimistically (D-08) + close now.
    onAdded(mangaBakaIds);
    onModalClose();
  }, [
    isRootFolderMissing,
    count,
    bulkAdd,
    mangaBakaIds,
    options,
    onAdded,
    onModalClose,
  ]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AddNManga', { count })}</ModalHeader>

      <ModalBody>
        <div data-testid="add-top-x-modal">
          <AddMangaFormBody
            addError={addError}
            countSummary={translate('DiscoveryBulkAddSummary', { count })}
          />
        </div>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div>
          <label className={styles.searchLabelContainer}>
            <span className={styles.searchLabel}>
              {translate('AddNewMangaSearchForMissingChapters')}
            </span>

            <CheckInput
              containerClassName={styles.searchInputContainer}
              className={styles.searchInput}
              name="searchForMissingChapters"
              value={options.searchForMissingChapters}
              onChange={handleSearchToggleChange}
            />
          </label>
        </div>

        <SpinnerButton
          className={styles.addButton}
          kind={kinds.SUCCESS}
          isSpinning={false}
          isDisabled={isRootFolderMissing || count === 0}
          data-testid="add-top-x-modal-add-button"
          onPress={handleAddPress}
        >
          {translate('AddNManga', { count })}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddTopXModalContent;
