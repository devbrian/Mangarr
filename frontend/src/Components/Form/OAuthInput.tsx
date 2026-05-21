import React, { useCallback, useEffect, useMemo } from 'react';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import { kinds } from 'Helpers/Props';
import useOAuth, { OAuthCompletionMode } from 'OAuth/useOAuth';
import AniListPinModal from 'Settings/ImportLists/AniList/AniListPinModal';
import MalCallbackUrlModal from 'Settings/ImportLists/MyAnimeList/MalCallbackUrlModal';
import { getValidationFailures } from 'Store/Selectors/selectSettings';
import { InputOnChange } from 'typings/inputs';
import { useFormInputGroup } from './FormInputGroupContext';

export interface OAuthInputProps {
  label?: string;
  name: string;
  provider: string;
  providerData: Record<string, unknown>;
  section?: string;
  onChange: InputOnChange<unknown>;
}

// GH #221 — derive the OAuth completion mode from the providerData.implementation
// field. The implementation name is the backend C# class name (e.g.
// "AniListImportList", "MalImportList") surfaced verbatim on the
// ImportListDefinition / NotificationDefinition / IndexerDefinition row.
//
// Default ("callback") preserves the Trakt-canonical behavior for every existing
// OAuth provider (Trakt notification, etc) — only paste-back providers need to
// branch.
function deriveCompletionMode(
  providerData: Record<string, unknown>
): OAuthCompletionMode {
  const implementation =
    typeof providerData?.implementation === 'string'
      ? (providerData.implementation as string)
      : '';

  switch (implementation) {
    case 'AniListImportList':
      return 'paste-pin';
    case 'MalImportList':
      return 'paste-callback-url';
    default:
      return 'callback';
  }
}

// GH #221 — extract the OAuth authorize URL the backend returned in
// `pendingPaste.oauthUrl`. The paste-back modal uses this so the user can
// re-open the provider's authorize page if they accidentally closed it.
function getProviderDisplayName(providerData: Record<string, unknown>): string {
  const implementationName =
    typeof providerData?.implementationName === 'string'
      ? (providerData.implementationName as string)
      : '';
  if (implementationName) {
    return implementationName;
  }
  const implementation =
    typeof providerData?.implementation === 'string'
      ? (providerData.implementation as string)
      : '';
  switch (implementation) {
    case 'AniListImportList':
      return 'AniList';
    case 'MalImportList':
      return 'MyAnimeList';
    default:
      return 'Provider';
  }
}

function OAuthInput({
  label = 'Start OAuth',
  name,
  provider,
  providerData,
  section,
  onChange,
}: OAuthInputProps) {
  const formInputActions = useFormInputGroup();
  const {
    authorizing,
    error,
    result,
    pendingPaste,
    startOAuth,
    completeOAuth,
    cancelOAuth,
    resetOAuth,
  } = useOAuth();

  const completionMode = useMemo(
    () => deriveCompletionMode(providerData),
    [providerData]
  );
  const providerDisplayName = useMemo(
    () => getProviderDisplayName(providerData),
    [providerData]
  );

  const handlePress = useCallback(() => {
    startOAuth({
      name,
      provider,
      providerData,
      section,
      completionMode,
    });
  }, [name, provider, providerData, section, completionMode, startOAuth]);

  useEffect(() => {
    if (!result) {
      return;
    }

    Object.keys(result).forEach((key) => {
      onChange({ name: key, value: result[key] });
    });
  }, [result, onChange]);

  useEffect(() => {
    return () => {
      resetOAuth();
    };
  }, [resetOAuth]);

  useEffect(() => {
    const validationFailures = getValidationFailures(error);

    formInputActions?.setClientErrors(validationFailures?.errors ?? []);
    formInputActions?.setClientWarnings(validationFailures?.warnings ?? []);
  }, [name, error, formInputActions]);

  // GH #221 — paste-back modal handlers. AniList dispatches `getAuthPin` with
  // the pasted pin; MAL dispatches `getOAuthToken` with the pasted callback URL
  // in the `redirectedUrl` query param. Both shapes match the backend's
  // RequestAction signature verbatim (see AniListImportList.cs:122,
  // MalImportList.cs:143).
  const handleAniListPinSubmit = useCallback(
    async (pin: string) => {
      await completeOAuth('getAuthPin', { pin });
    },
    [completeOAuth]
  );

  const handleMalCallbackUrlSubmit = useCallback(
    async (redirectedUrl: string) => {
      await completeOAuth('getOAuthToken', { redirectedUrl });
    },
    [completeOAuth]
  );

  const handleModalClose = useCallback(() => {
    cancelOAuth();
  }, [cancelOAuth]);

  const isPastePinOpen = !!pendingPaste && pendingPaste.mode === 'paste-pin';
  const isPasteCallbackUrlOpen =
    !!pendingPaste && pendingPaste.mode === 'paste-callback-url';

  return (
    <div>
      <SpinnerErrorButton
        kind={kinds.PRIMARY}
        isSpinning={authorizing}
        error={error}
        onPress={handlePress}
      >
        {label}
      </SpinnerErrorButton>

      {/*
        GH #221 — paste-back modals. Rendered conditionally based on
        completionMode. Cancel resets the OAuth state; Submit dispatches the
        provider-specific completion action.
      */}
      {completionMode === 'paste-pin' && (
        <AniListPinModal
          isOpen={isPastePinOpen}
          pinAuthorizeUrl={pendingPaste?.oauthUrl ?? ''}
          providerName={providerDisplayName}
          isSubmitting={authorizing && isPastePinOpen}
          error={error}
          onSubmit={handleAniListPinSubmit}
          onModalClose={handleModalClose}
        />
      )}

      {completionMode === 'paste-callback-url' && (
        <MalCallbackUrlModal
          isOpen={isPasteCallbackUrlOpen}
          authorizeUrl={pendingPaste?.oauthUrl ?? ''}
          providerName={providerDisplayName}
          isSubmitting={authorizing && isPasteCallbackUrlOpen}
          error={error}
          onSubmit={handleMalCallbackUrlSubmit}
          onModalClose={handleModalClose}
        />
      )}
    </div>
  );
}

export default OAuthInput;
