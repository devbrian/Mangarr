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

// Phase 27 Plan 27-04 Task 3 — MyAnimeList paste-the-callback-URL modal.
//
// D-09 paste-the-callback-URL UX flow:
//   1. User clicks the "Connect" button in the MAL ImportList Settings form
//      (FieldType.OAuth control on MalImportListSettings.SignIn).
//   2. The server's RequestAction("startOAuth") generates a fresh MalOAuthState
//      (random Verifier + StateNonce + 10-min TTL ExpiresAt), persists the JSON
//      blob on Settings.PendingPkceState, and returns
//      { OauthUrl: "https://myanimelist.net/v1/oauth2/authorize?...code_challenge_method=plain&state=..." }.
//   3. The FE opens that URL in a new tab; MyAnimeList authenticates the user and
//      asks them to grant access to the Mangarr app.
//   4. After consent, MAL redirects the browser to the registered redirect URI
//      (https://mangarr.local/oauth/mal/callback?code=X&state=Y). Mangarr does
//      NOT serve this URL — it 404s in the user's browser by design (paste-URL
//      UX intentionally renders the destination unreachable so the flow works
//      behind any reverse proxy / non-default port / containerized deployment).
//   5. The user copies the entire URL from their browser address bar (including
//      the `?code=...&state=...` query string) and pastes it into this modal.
//   6. On submit, the modal dispatches RequestAction("getOAuthToken",
//      { redirectedUrl }) to the provider. The server parses `code` + `state`
//      from the URL, validates the state nonce against Settings.PendingPkceState
//      (CSRF + 10-min TTL + single-use enforcement), exchanges the code +
//      verifier for the OAuth token block via the proxy, persists the tokens
//      on Settings, and CLEARS PendingPkceState.
//
// Reserved data-testid attributes (Plan 27-01 data-testid-spec.md reservation
// ledger for Phase 27):
//   * importlist-mal-callback-url-input — paste URL input field
//   * importlist-mal-callback-url-submit — submit button (dispatches getOAuthToken)
//   * importlist-mal-callback-url-cancel — cancel button (closes the modal)
//
// Component is standalone — the FE wiring that opens this modal in response to
// the FieldType.OAuth Connect-click is reserved for a follow-up plan (substrate
// touches to OAuthInput.tsx live outside Plan 27-04's scope, mirroring Plan 27-03
// AniListPinModal precedent). For Plan 27-04 the modal proves out the structure
// + data-testid ledger; integration with the startOAuth click handler ships when
// the substrate adds MAL-aware OAuth dispatch.

interface MalCallbackUrlModalProps {
  isOpen: boolean;
  authorizeUrl: string;
  providerName?: string;
  onSubmit: (redirectedUrl: string) => Promise<void> | void;
  onModalClose: () => void;
  isSubmitting?: boolean;
  error?: Error | null;
}

function MalCallbackUrlModal({
  isOpen,
  authorizeUrl,
  providerName = 'MyAnimeList',
  onSubmit,
  onModalClose,
  isSubmitting = false,
  error = null,
}: MalCallbackUrlModalProps) {
  const [redirectedUrl, setRedirectedUrl] = useState('');

  // Clear the pasted callback URL when the modal closes — reopens start fresh
  // (an expired OAuth `?code=…&state=…` cannot accidentally re-submit) and
  // sensitive query params don't linger in component state.
  useEffect(() => {
    if (!isOpen) {
      setRedirectedUrl('');
    }
  }, [isOpen]);

  const handleUrlChange = useCallback(
    ({ value }: { value: string | number | string[] }) => {
      setRedirectedUrl(String(value ?? ''));
    },
    []
  );

  const handleSubmitPress = useCallback(async () => {
    const trimmed = redirectedUrl.trim();
    if (!trimmed) {
      return;
    }
    await onSubmit(trimmed);
  }, [redirectedUrl, onSubmit]);

  return (
    <Modal size={sizes.MEDIUM} isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent
        data-testid="importlist-mal-callback-url-modal"
        onModalClose={onModalClose}
      >
        <ModalHeader>
          {translate('ImportListsMalSignInLabel')} — {providerName}
        </ModalHeader>

        <ModalBody>
          <p>
            <Link to={authorizeUrl}>
              {translate('ImportListsMalCallbackUrlPasteHelpText')}
            </Link>
          </p>

          <TextInput
            data-testid="importlist-mal-callback-url-input"
            name="redirectedUrl"
            value={redirectedUrl}
            placeholder={translate('ImportListsMalCallbackUrlInputPlaceholder')}
            autoFocus={true}
            onChange={handleUrlChange}
          />
        </ModalBody>

        <ModalFooter>
          <Button
            data-testid="importlist-mal-callback-url-cancel"
            onPress={onModalClose}
          >
            {translate('ImportListsMalCallbackUrlCancelLabel')}
          </Button>

          <SpinnerErrorButton
            data-testid="importlist-mal-callback-url-submit"
            isSpinning={isSubmitting}
            error={error}
            onPress={handleSubmitPress}
          >
            {translate('ImportListsMalCallbackUrlSubmitLabel')}
          </SpinnerErrorButton>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default MalCallbackUrlModal;
