import React, { useCallback, useEffect, useState } from 'react';
import { type Error } from 'App/State/AppSectionState';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

// Phase 27 Plan 27-03 Task 3 — AniList Auth Pin paste-back modal.
//
// D-07 paste-pin paste-back UX flow:
//   1. User clicks the "Connect" button in the AniList ImportList Settings form
//      (FieldType.OAuth control on AniListImportListSettings.SignIn).
//   2. The server's RequestAction("startOAuth") returns
//      { OauthUrl: "https://anilist.co/api/v2/oauth/pin?client_id=...&response_type=code" }.
//   3. The FE opens that URL in a new tab; AniList authenticates the user and
//      displays a numeric pin on the page.
//   4. The user copies the pin from the AniList tab and switches back to Mangarr.
//   5. This modal (AniListPinModal) opens with the pasted-pin input field; on
//      submit it dispatches `getAuthPin` to the provider, which exchanges the pin
//      for the 1-year JWT access token via the proxy and persists it on Settings.
//
// Reserved data-testid attributes (Plan 27-01 data-testid-spec.md reservation
// ledger for Phase 27):
//   * importlist-anilist-pin-input — pin paste input field
//   * importlist-anilist-pin-submit — submit button (exchanges the pin)
//   * importlist-anilist-pin-cancel — cancel button (closes the modal)
//
// Component is standalone — the FE wiring that opens this modal in response to
// the FieldType.OAuth Connect-click is reserved for a follow-up plan (substrate
// touches to OAuthInput.tsx live outside Plan 27-03's scope). For Plan 27-03 the
// modal proves out the structure + data-testid ledger; integration with the
// startOAuth click handler ships when the substrate adds AniList-aware OAuth
// dispatch.

interface AniListPinModalProps {
  isOpen: boolean;
  pinAuthorizeUrl: string;
  providerName?: string;
  onSubmit: (pin: string) => Promise<void> | void;
  onModalClose: () => void;
  isSubmitting?: boolean;
  error?: Error | null;
}

function AniListPinModal({
  isOpen,
  pinAuthorizeUrl,
  providerName = 'AniList',
  onSubmit,
  onModalClose,
  isSubmitting = false,
  error = null,
}: AniListPinModalProps) {
  const [pin, setPin] = useState('');

  // Clear pin state when the modal closes so a reopen starts fresh and
  // previously-entered sensitive input doesn't linger in component state.
  useEffect(() => {
    if (!isOpen) {
      setPin('');
    }
  }, [isOpen]);

  const handlePinChange = useCallback(
    ({ value }: { value: string | number | string[] }) => {
      setPin(String(value ?? ''));
    },
    []
  );

  const handleSubmitPress = useCallback(async () => {
    const trimmed = pin.trim();
    if (!trimmed) {
      return;
    }
    await onSubmit(trimmed);
  }, [pin, onSubmit]);

  return (
    <Modal size={sizes.SMALL} isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent
        data-testid="importlist-anilist-pin-modal"
        onModalClose={onModalClose}
      >
        <ModalHeader>
          {translate('ImportListsAniListSignInLabel')} — {providerName}
        </ModalHeader>

        <ModalBody>
          <p>
            <Link to={pinAuthorizeUrl}>
              {translate('ImportListsAniListPinPasteHelpText')}
            </Link>
          </p>

          <TextInput
            data-testid="importlist-anilist-pin-input"
            name="pin"
            value={pin}
            placeholder={translate('ImportListsAniListPinInputPlaceholder')}
            autoFocus={true}
            onChange={handlePinChange}
          />
        </ModalBody>

        <ModalFooter>
          <Button
            data-testid="importlist-anilist-pin-cancel"
            onPress={onModalClose}
          >
            {translate('Cancel')}
          </Button>

          <SpinnerErrorButton
            data-testid="importlist-anilist-pin-submit"
            isSpinning={isSubmitting}
            error={error}
            onPress={handleSubmitPress}
          >
            {translate('Save')}
          </SpinnerErrorButton>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default AniListPinModal;
