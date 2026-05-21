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
// "internal":              GH #229 MangaDex D-08 internal-only flow. The backend performs the
//                          password-grant server-side and returns the FINAL envelope
//                          `{ success, authUser, expires, accessToken, refreshToken }` directly
//                          from startOAuth. The hook DOES NOT open a popup, DOES NOT dispatch
//                          continueOAuth, and DOES NOT call getOAuthToken — the start-response
//                          IS the result and is written into the form via the onChange handler
//                          (OAuthInput.tsx). `success: false` surfaces the `error` field as a
//                          validation banner.
export type OAuthCompletionMode =
  | 'callback'
  | 'paste-pin'
  | 'paste-callback-url'
  | 'internal';

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

        // GH #229 — MangaDex D-08 internal-only flow. The backend has already done
        // the password-grant and the response envelope IS the final result. No popup,
        // no continueOAuth round-trip, no getOAuthToken — just surface the envelope
        // to the caller via the result-state so OAuthInput's onChange handler writes
        // each key (authUser/expires/accessToken/refreshToken) into the form.
        if (completionMode === 'internal') {
          // Require explicit success=true and the actual token fields before treating the
          // envelope as a successful exchange — a malformed response (e.g. backend omits
          // success flag, or returns success=true with no tokens) must surface as an error
          // instead of silently writing garbage into the form.
          const internalResponse = response as OAuthResponse & {
            error?: string;
            authUser?: string;
            expires?: string;
            accessToken?: string;
            refreshToken?: string;
          };
          const isSuccess = internalResponse.success === true;
          const accessToken = internalResponse.accessToken ?? '';
          const refreshToken = internalResponse.refreshToken ?? '';

          if (!isSuccess || !accessToken || !refreshToken) {
            const message =
              internalResponse.error ??
              'OAuth start failed — the backend did not complete the internal flow';
            const startError = Object.assign(new Error(message), {
              status: 400,
              responseJSON: [
                {
                  propertyName: name,
                  errorMessage: message,
                },
              ],
            });
            throw startError;
          }

          // Map only the documented OAuthResult fields — never pass the raw envelope so
          // unrelated keys (success/error markers) don't get written into the form state.
          const sanitizedResult: OAuthResult = {
            accessToken,
            refreshToken,
          };
          if (internalResponse.authUser) {
            sanitizedResult.authUser = internalResponse.authUser;
          }
          if (internalResponse.expires) {
            sanitizedResult.expires = internalResponse.expires;
          }

          setOAuthValue({
            authorizing: false,
            result: sanitizedResult,
            error: null,
            pendingPaste: null,
          });
          return response;
        }

        // GH #221 — paste-back flows (AniList paste-pin, MAL paste-callback-URL).
        // Open the provider's authorize URL in a new tab and PAUSE — the hook does
        // NOT poll for a callback (none ever fires). Surface state so the caller
        // can render the appropriate paste-back modal. The flow resumes when the
        // caller invokes `completeOAuth(payload)` with the user-pasted value.
        if (completionMode !== 'callback') {
          // Backend convention: paste-back providers may return
          // `{ success: false, error: "..." }` on a startOAuth call that cannot
          // proceed (e.g. MAL requires the ImportList to be saved first so
          // PendingPkceState can round-trip across the request boundary). Surface
          // the actionable error message verbatim so the user sees the guidance.
          if (response.success === false) {
            const message =
              (response as { error?: string }).error ??
              'OAuth start failed — the backend did not return an authorize URL';
            const startError = Object.assign(new Error(message), {
              status: 400,
              responseJSON: [
                {
                  propertyName: name,
                  errorMessage: message,
                },
              ],
            });
            throw startError;
          }

          if (!response.oauthUrl) {
            const message =
              'Backend did not return an oauthUrl for paste-back OAuth flow';
            const startError = Object.assign(new Error(message), {
              status: 400,
              responseJSON: [
                {
                  propertyName: name,
                  errorMessage: message,
                },
              ],
            });
            throw startError;
          }

          // Open the provider URL in a new tab. Use the default popup target
          // (matches the Trakt-canonical showOAuthWindow shape at line 86) so
          // browser behavior is consistent — passing '_blank' caused chromium in
          // headless Playwright to mark the cross-origin popup `closed=true`
          // immediately on return, tripping the popup-blocker false-positive.
          const newWindow = window.open(response.oauthUrl);

          // Strict-only popup-blocker check: only flag `newWindow == null` as
          // "blocked". Some browsers (notably headless chromium under Playwright)
          // synchronously expose `closed=true` for cross-origin popups even when
          // the popup successfully opens — the original `closed`/`typeof
          // undefined` checks (preserved on showOAuthWindow at line 88-92) are
          // wrong for the paste-back flow because they would suppress the modal.
          // Trade-off: a genuinely-blocked popup may show "no modal opens"
          // instead of the explicit blocker error — better than the false
          // positive that hides the entire paste-back flow under Playwright.
          if (!newWindow) {
            const message = 'Pop-ups are being blocked by your browser';
            const error = Object.assign(new Error(message), {
              status: 400,
              responseJSON: [
                {
                  propertyName: name,
                  errorMessage: message,
                },
              ],
            });
            throw error;
          }

          // GH #221 PR #228 P1 fix (chatgpt-codex review): set `authorizing`
          // FALSE while waiting for the user to paste — otherwise the modal's
          // submit SpinnerErrorButton starts in a spinning/disabled state and
          // the user cannot submit. `authorizing` is the "backend request in
          // flight" contract; the request just completed (startOAuth returned
          // the oauthUrl) and the hook is now PAUSED until the user submits.
          // It flips back to true inside `completeOAuth` for the duration of
          // the completion round-trip. The modal is rendered via
          // `pendingPaste != null`, and the Edit modal's backdrop blocks
          // re-clicks on the parent Connect button while the paste modal is
          // open — no additional spinner is needed on the parent.
          setOAuthValue({
            authorizing: false,
            pendingPaste: {
              mode: completionMode as 'paste-pin' | 'paste-callback-url',
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
