// Sonarr divergence: Phase 25 Plan 25-03 Task 1 — modal-content collapsed
// into a thin wrapper around the new shared InteractiveImportContent. See
// DIVERGENCE.md / 25-03-PLAN.md / 25-03-SUMMARY.md.
//
// Before Plan 25-03 this file owned the entire manual-import body (~1140
// lines). The body was extracted into InteractiveImport/Interactive/
// InteractiveImportContent.tsx so the new full-page route /add/import
// (InteractiveImportPage.tsx, D-02) can render the same content WITHOUT
// modal chrome, while the existing Wanted/Missing modal surface (D-03)
// keeps modal UX by re-wrapping the shared content with the Modal* chrome
// components below.
//
// The exported component name and props interface are preserved (extends
// the new InteractiveImportContentProps with the modal-only header/close
// props) so InteractiveImportModal.tsx (the parent <Modal> wrapper) does
// not need to change.
import React from 'react';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import { scrollDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import InteractiveImportContent, {
  InteractiveImportContentProps,
} from './InteractiveImportContent';

export interface InteractiveImportModalContentProps
  extends InteractiveImportContentProps {
  modalTitle: string;
  onModalClose(): void;
}

function InteractiveImportModalContent(
  props: InteractiveImportModalContentProps
) {
  const { modalTitle, onModalClose, title, folder, ...contentProps } = props;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {modalTitle ?? translate('ManualImport')}
        {title || folder ? ` - ${title || folder}` : null}
      </ModalHeader>

      <ModalBody scrollDirection={scrollDirections.BOTH}>
        <InteractiveImportContent
          {...contentProps}
          title={title}
          folder={folder}
          headerLabel={modalTitle}
          onCancel={onModalClose}
        />
      </ModalBody>
    </ModalContent>
  );
}

export default InteractiveImportModalContent;
