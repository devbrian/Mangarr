// Phase 27.1 Plan 27.1-03 — slim shell per Sonarr v5-develop shell+content split.
import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditImportListExclusionModalContent from './EditImportListExclusionModalContent';

interface EditImportListExclusionModalProps {
  id?: number;
  title?: string;
  mangaDexId?: string;
  malId?: number;
  aniListId?: number;
  mangaBakaId?: number;
  isOpen: boolean;
  onModalClose: () => void;
  onDeleteImportListExclusionPress?: () => void;
}

function EditImportListExclusionModal({
  isOpen,
  onModalClose,
  ...otherProps
}: EditImportListExclusionModalProps) {
  return (
    <Modal size={sizes.MEDIUM} isOpen={isOpen} onModalClose={onModalClose}>
      <EditImportListExclusionModalContent
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default EditImportListExclusionModal;
