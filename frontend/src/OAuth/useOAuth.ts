import { useCallback, useState } from 'react';
import { type Error } from 'App/State/AppSectionState';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import requestAction from 'Utilities/requestAction';

const callbackUrl = `${window.location.origin}${window.Mangarr.urlBase}/oauth.html`;

// GH #221 — OAuth completion-mode discriminant.
//
// "callback":              Trakt-canonical auto-callback flow. The new tab navigates to
//                          a Mangarr-served `/oauth.html` shim that calls
//                          `window.opener.onCompleteOauth(query)`; the hook then dispatches
//                          `getOAuthToken` automatically with the collected query params.
//                          Used by every Sonarr-heritage OAuth provider (Trakt, etc).
// "paste-pin":             AniList paste-pin flow (D-07). The new tab opens to AniList's
//                          /oauth/pin URL where the user reads a numeric pin off the page.
//                          The hook DOES NOT wait for a callback (none fires) — it returns
//                          immediately after opening the window, surfaces a paste-back modal
//                          in the original tab, and the caller invokes `completeOAuth({ pin })`
//                          when the user submits. The hook dispatches `getAuthPin` with the
//                          pasted pin.
// "paste-callback-url":    MAL paste-the-callback-URL flow (D-09). The new tab opens to MAL's
//                          authorize URL with PKCE state. After consent, MAL redirects to an
//                          intentionally-unreachable URL (`https://mangarr.local/oauth/mal/callback`);
//                          the user copies the full URL from their browser bar and pastes it
//                          into a modal. The hook dispatches `getOAuthToken` with the full
//                          URL in the `redirectedUrl` query param so the backend can parse
//                          `?code=&state=` and validate the PKCE state nonce.
export type OAuthCompletionMode =
  | 'callback'
  | 'paste-pin'
  | 'paste-callback-url';

interface OAuthResult {
  [key: string]: string | number | boolean;
}

interface OAuthState {
  authorizing: boolean;
  result: OAuthResult | null;
  error: Error | null;
  // Paste-back state — only populated when completionMode is "paste-pin" or
  // "paste-callback-url" and the new tab has been opened but the user has not
  // yet submitted the paste-back modal. Cleared on submit/cancel/reset.
  pendingPaste: {
    mode: 'paste-pin' | 'paste-callback-url';
    oauthUrl: string;
    payload: StartOAuthParams;
    startResponse: OAuthResponse;
  } | null;
}

interface StartOAuthParams {
  name: string;
  provider?: string;
  providerData?: Record<string, unknown>;
  // GH #221 — caller may pin the completion mode. Defaults to "callback" for
  // backward compatibility with every existing OAuth provider.
  completionMode?: OAuthCompletionMode;
  [key: string]: unknown;
}

interface OAuthResponse {
  oauthUrl?: string;
  poll?: boolean;
  success?: boolean;
  [key: string]: unknown;
}

interface QueryParams {
  [key: string]: string;
}

interface WindowWithOAuth extends Window {
  onCompleteOauth?: (query: string, onComplete: () => void) => void;
}

function showOAuthWindow(
  url: string,
  payload: StartOAuthParams,
  poll = false,
  ajaxOptions?: Record<string, unknown>
): Promise<QueryParams> {
  return new Promise((resolve, reject) => {
    const selfWindow = window as WindowWithOAuth;
    const newWindow = window.open(url);

    if (
      !newWindow ||
      newWindow.closed ||
      typeof newWindow.closed === 'undefined'
    ) {
      // A fake validation error to mimic a 400 response from the API.
      const error = Object.assign(
        new Error('Pop-ups are being blocked by your browser'),
        {
          status: 400,
          responseJSON: [
            {
              propertyName: payload.name,
              errorMessage: 'Pop-ups are being blocked by your browser',
            },
          ],
        }
      );

      return reject(error);
    }

    if (poll) {
      const pollAction = () => {
        requestAction({
          action: 'pollOAuth',
          queryParams: ajaxOptions,
          ...payload,
        }).then((response: OAuthResponse) => {
          if (response.success) {
            resolve({});
          } else {
            setTimeout(() => {
              pollAction();
            }, 5000);
          }
        });
      };

      setTimeout(() => {
        pollAction();
      }, 5000);
    } else {
      selfWindow.onCompleteOauth = function (
        query: string,
        onComplete: () => void
      ) {
        delete selfWindow.onCompleteOauth;

        const queryParams: Record<string, string> = {};
        const splitQuery = query.substring(1).split('&');

        splitQuery.forEach((param) => {
          if (param) {
            const paramSplit = param.split('=');
            queryParams[paramSplit[0]] = paramSplit[1];
          }
        });

        onComplete();
        resolve(queryParams);
      };
    }
  });
}

function executeIntermediateRequest(
  payload: Record<string, unknown>,
  ajaxOptions: Record<string, unknown>
): Promise<OAuthResponse> {
  return createAjaxRequest(ajaxOptions).request.then(
    (data: Record<string, unknown>) => {
      return requestAction({
        action: 'continueOAuth',
        queryParams: {
          ...data,
          callbackUrl,
        },
        ...payload,
      });
    }
  );
}

const useOAuth = () => {
  const [oAuthState, setOAuthState] = useState<OAuthState>({
    authorizing: false,
    result: null,
    error: null,
    pendingPaste: null,
  });

  const setOAuthValue = useCallback((values: Partial<OAuthState>) => {
    setOAuthState((prev) => ({ ...prev, ...values }));
  }, []);

  const resetOAuth = useCallback(() => {
    setOAuthState({
      authorizing: false,
      result: null,
      error: null,
      pendingPaste: null,
    });
  }, []);

  const startOAuth = useCallback(
    async (params: StartOAuthParams) => {
      const { name, completionMode = 'callback', ...otherPayload } = params;

      const actionPayload = {
        action: 'startOAuth',
        queryParams: { callbackUrl },
        ...otherPayload,
      };

      setOAuthValue({ authorizing: true, error: null, pendingPaste: null });

      try {
        let startResponse: OAuthResponse = {};

        const response = (await requestAction(actionPayload)) as OAuthResponse;
        startResponse = response;

        // GH #221 — paste-back flows (AniList paste-pin, MAL paste-callback-URL).
        // Open the provider's authorize URL in a new tab and PAUSE — the hook does
        // NOT poll for a callback (none ever fires). Surface state so the caller
        // can render the appropriate paste-back modal. The flow resumes when the
        // caller invokes `completeOAuth(payload)` with the user-pasted value.
        if (completionMode !== 'callback') {
          if (!response.oauthUrl) {
            throw new Error(
              'Backend did not return an oauthUrl for paste-back OAuth flow'
            );
          }

          // Open the provider URL in a new tab. Same window.open semantics as
          // showOAuthWindow (uses default popup target) — pop-up blocker detection
          // mirrors the callback-mode path.
          const newWindow = window.open(response.oauthUrl, '_blank');
          if (
            !newWindow ||
            newWindow.closed ||
            typeof newWindow.closed === 'undefined'
          ) {
            const error = Object.assign(
              new Error('Pop-ups are being blocked by your browser'),
              {
                status: 400,
                responseJSON: [
                  {
                    propertyName: name,
                    errorMessage: 'Pop-ups are being blocked by your browser',
                  },
                ],
              }
            );
            throw error;
          }

          // Persist pending-paste state. `authorizing` stays true so the
          // SpinnerErrorButton continues to spin until the user submits the
          // paste-back modal. The caller (OAuthInput.tsx) reads `pendingPaste`
          // to decide which modal to render.
          setOAuthValue({
            authorizing: true,
            pendingPaste: {
              mode: completionMode,
              oauthUrl: response.oauthUrl,
              payload: params,
              startResponse,
            },
          });
          return null;
        }

        let queryParams: QueryParams | null = null;

        if (response.oauthUrl) {
          queryParams = await showOAuthWindow(response.oauthUrl, params);
        } else {
          const intermediateResponse = await executeIntermediateRequest(
            otherPayload,
            response // Pass the entire response as ajaxOptions
          );
          startResponse = intermediateResponse;

          if (!intermediateResponse.oauthUrl) {
            throw new Error('No OAuth URL received from intermediate request');
          }

          queryParams = await showOAuthWindow(
            intermediateResponse.oauthUrl,
            params,
            intermediateResponse.poll || false,
            intermediateResponse
          );
        }

        const tokenResponse = await requestAction({
          action: 'getOAuthToken',
          queryParams: {
            ...startResponse,
            ...queryParams,
          },
          ...otherPayload,
        });

        setOAuthValue({
          authorizing: false,
          result: tokenResponse,
          error: null,
          pendingPaste: null,
        });

        return tokenResponse;
      } catch (error) {
        const oAuthError = error as Error;
        setOAuthValue({
          authorizing: false,
          result: null,
          error: oAuthError,
          pendingPaste: null,
        });

        throw error;
      }
    },
    [setOAuthValue]
  );

  // GH #221 — completes a paste-back OAuth flow. Caller invokes this after the
  // user submits the paste-back modal (AniListPinModal or MalCallbackUrlModal).
  //
  // `pasteValues` is whatever query-param shape the backend expects:
  //   * AniList:  { pin: "ABC123" }       → backend RequestAction("getAuthPin",   query["pin"])
  //   * MAL:      { redirectedUrl: "..." } → backend RequestAction("getOAuthToken", query["redirectedUrl"])
  //
  // `action` is "getAuthPin" for AniList per its backend RequestAction surface,
  // "getOAuthToken" for MAL (matches Trakt-canonical action name).
  const completeOAuth = useCallback(
    async (action: string, pasteValues: Record<string, string>) => {
      const pending = oAuthState.pendingPaste;
      if (!pending) {
        const error = Object.assign(
          new Error('No pending OAuth flow to complete'),
          { status: 400 }
        );
        setOAuthValue({ error: error as unknown as Error });
        throw error;
      }

      setOAuthValue({ authorizing: true, error: null });

      try {
        // Strip the hook-internal `name` label + `completionMode` discriminant
        // from the outbound payload — neither is a backend query param. The
        // unused destructured siblings are exempt from no-unused-vars via the
        // eslint config's `ignoreRestSiblings: true` rule.
        const {
          name: _name,
          completionMode: _completionMode,
          ...otherPayload
        } = pending.payload;

        const tokenResponse = await requestAction({
          action,
          queryParams: {
            ...pending.startResponse,
            ...pasteValues,
          },
          ...otherPayload,
        });

        // Backend convention: paste-back actions return `{ success: false, error: "..." }`
        // on user-error / state-mismatch (validated PKCE state, expired TTL, missing
        // query param, etc.). Surface this as an error so the modal can show it and
        // the user can retry without losing the rest of the flow.
        if (
          tokenResponse &&
          typeof tokenResponse === 'object' &&
          (tokenResponse as { success?: boolean }).success === false
        ) {
          const errMessage =
            (tokenResponse as { error?: string }).error ??
            'OAuth completion failed';
          const error = Object.assign(new Error(errMessage), {
            status: 400,
            responseJSON: [
              {
                propertyName: pending.payload.name,
                errorMessage: errMessage,
              },
            ],
          });
          setOAuthValue({
            authorizing: false,
            error: error as unknown as Error,
          });
          throw error;
        }

        setOAuthValue({
          authorizing: false,
          result: tokenResponse,
          error: null,
          pendingPaste: null,
        });

        return tokenResponse;
      } catch (error) {
        const oAuthError = error as Error;
        setOAuthValue({
          authorizing: false,
          error: oAuthError,
        });
        throw error;
      }
    },
    [oAuthState.pendingPaste, setOAuthValue]
  );

  // GH #221 — cancels a pending paste-back flow without dispatching a completion
  // action. Used by the modal's Cancel button. Resets `authorizing` so the
  // SpinnerErrorButton stops spinning and the user can re-click Connect.
  const cancelOAuth = useCallback(() => {
    setOAuthState((prev) => ({
      ...prev,
      authorizing: false,
      pendingPaste: null,
      error: null,
    }));
  }, []);

  return {
    ...oAuthState,
    startOAuth,
    completeOAuth,
    cancelOAuth,
    setOAuthValue,
    resetOAuth,
  };
};

export default useOAuth;
