// Sonarr divergence: NEW manga sibling per Phase 7 D-05 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Settings/Profiles/Quality/EditQualityProfileModal.tsx (lines 1-58).
//
// UI-07: Edit modal wrapper for the CustomFormatProfile editor (Phase 5 Plan 05-03 entity).
//
// Manga sibling preserves: Modal scaffold (size + isOpen + onModalClose) + clearPendingChanges-on-close pattern;
// auto/measured height pattern.
//
// Manga sibling diverges from EditQualityProfileModal:
//   * Pending-changes Redux section name swapped: 'settings.qualityProfiles' → 'settings.customFormatProfiles'
//   * No clone-id support in v1
//
// Phase 8 cleanup: this stays — manga-canonical.

import React, { useCallback, useState } from 'react';
import { useDispatch } from 'react-redux';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import EditCustomFormatProfileModalContent from './EditCustomFormatProfileModalContent';

interface EditCustomFormatProfileModalProps {
  id?: number;
  isOpen: boolean;
  onModalClose: () => void;
}

function EditCustomFormatProfileModal({
  id,
  isOpen,
  onModalClose,
}: EditCustomFormatProfileModalProps) {
  const dispatch = useDispatch();
  const [height, setHeight] = useState<'auto' | number>('auto');

  const handleOnModalClose = useCallback(() => {
    dispatch(
      clearPendingChanges({ section: 'settings.customFormatProfiles' })
    );
    onModalClose();
  }, [dispatch, onModalClose]);

  const handleContentHeightChange = useCallback(
    (newHeight: number) => {
      if (height === 'auto' || newHeight !== 0) {
        setHeight(newHeight);
      }
    },
    [height]
  );

  return (
    <Modal
      style={{ height: height === 'auto' ? 'auto' : `${height}px` }}
      isOpen={isOpen}
      size={sizes.LARGE}
      onModalClose={handleOnModalClose}
    >
      <EditCustomFormatProfileModalContent
        id={id}
        onContentHeightChange={handleContentHeightChange}
        onModalClose={handleOnModalClose}
      />
    </Modal>
  );
}

export default EditCustomFormatProfileModal;
