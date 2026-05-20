# ImportLists/MyAnimeList (Phase 27 Plan 27-04)

## Purpose

MyAnimeList (MAL) list ImportList provider plugin. Pulls the user's MAL
manga list (filtered to a single `MalListStatus` per ImportList per D-10)
via the MAL v2 REST API, projects each list entry onto an
`ImportListItemInfo` row, and feeds it through the Phase 26 substrate's
dedup + exclusion + add-manga cascade.

Implements requirement IL-11 (per-user MAL list importer).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\ImportLists\MyAnimeList`

## Key Files

| File | Purpose |
|------|---------|
| `MalImportList.cs` | Concrete `OAuthAwareImportListBase<MalImportListSettings>` plugin (extends the Plan 27-01 base). Overrides `RequestAction` for the D-09 paste-the-callback-URL flow (`startOAuth` → generates fresh PKCE state, persists `Settings.PendingPkceState` JSON blob, returns `{ OauthUrl: authorize URL with code_challenge_method=plain + state nonce }`; `getOAuthToken` → parses `?code=X&state=Y` from `query["redirectedUrl"]`, validates CSRF + 10-min TTL via `MalOAuthState.IsValid`, exchanges code+verifier for tokens via proxy, persists tokens, CLEARS PendingPkceState — single-use enforcement). `RefreshToken()` override applies Trakt.cs:151 verbatim null-coalesce (MAL ROTATES refresh tokens). `FetchImportListResponse` override adds the D-05 reactive 401-retry decorator at the per-request level (force-expires Settings.Expires, calls RefreshTokenIfNecessary, re-attaches the rotated bearer, retries once). |
| `MalListStatus.cs` | D-10 single-select enum (5 values: `Reading`/`PlanToRead`/`Completed`/`OnHold`/`Dropped`). C# PascalCase with `[EnumMember(Value="snake_case")]` mapping to MAL API's snake_case query-string values. |
| `MalImportListSettings.cs` | Settings POCO. Implements `IOAuthImportListSettings` per Plan 27-01 base contract. User-visible field: `Status` enum (index 0, `FieldType.Select`). Hidden token block: `AccessToken`/`RefreshToken`/`Expires`/`PendingPkceState`/`AuthUser` (indices 1-5 with `Hidden=HiddenType.Hidden` + `Privacy=PrivacyLevel.Password`). `SignIn` at index 6 (`FieldType.OAuth`, "Connect" button). `PendingPkceState` is a JSON-serialized `MalOAuthState` blob held as `string` per Discretion #2 shape (a). No ClientId/ClientSecret fields — MAL ClientId is Mangarr-pinned compiled-in (`MalConstants.ClientId`) per public-client PKCE semantics. |
| `MalOAuthState.cs` | Transient PKCE flow state POCO. `Create()` generates 32-byte `StateNonce` (CSRF) + 48-byte `Verifier` (PKCE) via `System.Security.Cryptography.RandomNumberGenerator.GetBytes` (CSPRNG — ASVS V6) + 10-min TTL `ExpiresAt`. `IsValid(presented)` enforces ordinal-equal state nonce + non-expired TTL. base64url encoding per RFC 7636 §A. Single-use enforcement is by caller clearing `Settings.PendingPkceState = null` after a successful exchange. |
| `MalConstants.cs` | Mangarr-pinned compiled-in OAuth endpoint URLs + `ClientId` + redirect URI + state TTL + PKCE byte-length constants. `RedirectUri = "https://mangarr.local/oauth/mal/callback"` is INTENTIONALLY a URL Mangarr does NOT serve (paste-URL UX renders the destination unreachable so the flow works behind any reverse proxy / non-default port). `ClientId` is a placeholder until release-build executor swaps in the production MAL-issued value. |
| `MalImportListProxy.cs` | OAuth + manga-list HTTP proxy with 4 methods: `BuildAuthorizeUrl(state)` (deterministic URL construction; no HTTP call), `ExchangeCodeForToken(code, verifier)` (POST form-urlencoded grant_type=authorization_code), `RefreshAccessToken(refreshToken)` (POST form-urlencoded grant_type=refresh_token), `GetUserMangaList(settings, nextCursor)` (GET /v2/users/@me/mangalist with Bearer auth + paging.next cursor support). Every outbound request flows through `ApplySharedHeaders()` which sets `RateLimitKey="myanimelist"` (NEW SourceKey bucket per CONTEXT line 36) + honest Mangarr UA + `Accept: application/json`. T-V7: NEVER logs code/verifier/token values. |
| `MalImportListRequestGenerator.cs` | Single-page `ImportListPageableRequestChain` builder. Initial request to `/v2/users/@me/mangalist?status={value}&limit=1000&offset=0&fields=list_status,num_chapters` with Bearer-token attach + `RateLimitKey="myanimelist"`. MAL cursor pagination is server-driven via `paging.next` (the provider's parser DOES NOT walk it; the substrate's `HttpImportListBase.FetchItems` loop terminates on partial pages from the initial request — for v1.1 this is acceptable for typical MAL list sizes < 1000 entries). |
| `MalImportListParser.cs` | Walks `MalMangaListResource.Data[].Node` projecting each to `ImportListItemInfo { Title, MalId }`. Null-safe at every nesting level. Rejects rows lacking BOTH Title AND `Node.Id > 0`. |
| `Resource/MalTokenResponse.cs` | OAuth2 token-exchange DTO. `[JsonProperty]` snake_case for `token_type` / `expires_in` / `access_token` / `refresh_token`. |
| `Resource/MalMangaListResource.cs` | DTO for `/v2/users/@me/mangalist` response. Walks `data[].node.{id,title,main_picture}` + `data[].list_status.{status,score,num_chapters_read,is_rereading,updated_at}` + `paging.next` cursor. |

## Patterns / Conventions

- **OAuth shape — paste-the-callback-URL** (D-09): No Mangarr-side `/callback` HTTP listener. The flow:
  1. User clicks Connect → server `RequestAction("startOAuth")` → returns MAL authorize URL with `code_challenge_method=plain` + 32-byte state nonce + Mangarr-pinned ClientId + RedirectUri.
  2. FE opens that URL in a new tab → MAL authenticates user + asks for consent → MAL redirects browser to `https://mangarr.local/oauth/mal/callback?code=X&state=Y` (intentionally unreachable).
  3. User copies entire URL from browser bar → pastes into `MalCallbackUrlModal` → modal dispatches `RequestAction("getOAuthToken", { redirectedUrl })`.
  4. Server parses `code` + `state`, validates state nonce CSRF + TTL, exchanges code+verifier for tokens via proxy, persists tokens, clears `PendingPkceState`.
- **PKCE shape: `code_challenge_method=plain`** (NOT the hashed variant): MAL constraint, two independent sources confirm. Plain is weaker than the hashed variant but is the only method MAL accepts (T-V2 ACCEPT per threat model). Documented in MalConstants + MalOAuthState code comments.
- **MAL ClientId is Mangarr-pinned compiled-in** (per D-09 + Pattern D + RESEARCH §Open Question 4 + Sonarr-Trakt TraktProxy.cs:26 precedent). NOT user-supplied. Public-client PKCE flow has NO client_secret. See MalConstants.cs for the placeholder + release-build TODO.
- **NEW SourceKey `"myanimelist"`** (Pitfall 4 / CONTEXT line 36): MAL does NOT share a SourceKey with any other Mangarr caller (unlike MangaDex which shares across MetadataSource/Indexer/Downloader). The bucket is fresh per Phase 27 charter; 30 req/min default ceiling. Every outbound request from the proxy + request generator sets `RateLimitKey="myanimelist"` — Plan 27-05 close-out audit gate enforces exclusivity.
- **D-10 single-select Status enum**: `[FieldDefinition(Type = FieldType.Select, SelectOptions = typeof(MalListStatus))]` renders as a 5-option dropdown. Users who want multiple statuses create multiple ImportLists (one per status). Multi-select is a v1.2+ enhancement per CONTEXT line 442.
- **D-01 Sonarr-canonical plaintext token storage**: AccessToken / RefreshToken / Expires / AuthUser / PendingPkceState stored as plain `string` / `DateTime` on the Settings JSON column. Zero crypto-at-rest infrastructure (no key-protection provider, no platform secret-store bridge, no per-row sealed blob). UI hides the fields via `Hidden = HiddenType.Hidden` + `Privacy = PrivacyLevel.Password`; V5 controller `ProviderControllerBase` strips Hidden fields from outbound schema JSON.
- **D-05 + Pitfall 9 mitigation (inherited from base + MAL is the PRIMARY consumer)**: `OAuthAwareImportListBase.RefreshTokenIfNecessary()` wraps `RefreshToken()` in a per-`Definition.Id` `SemaphoreSlim`. MAL ROTATES refresh tokens on every grant call — concurrent refresh on the same Definition.Id would revoke the prior token cascading into `400 invalid_grant` for sibling threads holding the now-stale token. The semaphore + in-lock re-check (peer-flow defense) collapses 8 parallel callers into exactly ONE refresh call (proven by `concurrent_refresh_invocations_serialize_via_semaphore` test in MalImportListFixture).
- **D-05 reactive 401-retry decorator**: `FetchImportListResponse` override (NOT `Fetch` wrap — the inherited `HttpImportListBase.FetchItems` catches HttpException at the loop level and never bubbles to wrapping `Fetch`) catches 401 at the per-request level, force-expires Settings.Expires, calls `RefreshTokenIfNecessary`, re-attaches the rotated bearer token to the in-flight `HttpRequest.Headers["Authorization"]`, retries once.
- **CSRF + Replay defense** (T-V11): 32-byte CSPRNG state nonce per OAuth flow; `IsValid()` enforces ordinal-equal + non-expired check; `PendingPkceState` cleared on first successful exchange (single-use). `state_validation_rejects_mismatched_nonce` + `state_validation_rejects_expired_nonce` unit tests verify the gates.
- **Honest UA** (Phase 1 D-13): `User-Agent = $"Mangarr/{BuildInfo.Version.ToString(2)}"` on every outbound request (proxy + request generator both set this via shared helper). MAL ToS encourages honest UA disclosure.

## MAL OAuth-app Registration

`MalConstants.ClientId` is Mangarr-pinned and compiled-in (NOT user-supplied per Sonarr-Trakt precedent). Before shipping a release build:

1. Register a Mangarr application at <https://myanimelist.net/apiconfig>.
2. Set the App Redirect URL to EXACTLY `https://mangarr.local/oauth/mal/callback`.
   This URL is intentionally unreachable — paste-URL UX (D-09) intentionally
   renders the destination URL a 404 so the OAuth flow works behind any
   reverse proxy / non-default port / containerized deployment without
   per-deployment configuration.
3. Replace the `ClientId` constant in `MalConstants.cs` with the MAL-issued
   `client_id`. The constant is the placeholder string
   `"REPLACE_AT_RELEASE_BUILD_WITH_MAL_PINNED_CLIENT_ID"` until updated.
4. The MAL ClientId is NOT a secret per OAuth2 public-client semantics
   (PKCE replaces the shared-secret defense). It identifies the Mangarr
   app to MAL, not the user — so compiling it into the binary is the
   correct shape (mirrors Sonarr-Trakt's TraktProxy.cs:26 pattern).

Note: dev builds carrying the placeholder will receive `401 invalid_client`
on the first OAuth attempt — by design, so accidental code-exchange under
a real user's MAL account is impossible until the executor explicitly
swaps in the production ClientId.

## Manga Adaptation Notes

- The list is filtered to a single `MalListStatus` per ImportList per D-10 — Sonarr-canonical Trakt user-list pattern. Users segregate their currently-reading vs plan-to-read vs completed lists by creating multiple ImportLists.
- Cross-source ID projection: MAL's `node.id` is typed `int` (NOT stringly-typed like MangaDex `links.mal`); promoted directly to `ImportListItemInfo.MalId`. The cross-source resolver in `ImportListSyncService` later promotes the partial-ID row (Title + MalId only — MAL exposes no AniListId or MangaDexId) to a full `Manga` aggregate by querying MangaDex/AniList with the resolved MalId via `SearchForNewMangaByMalId` (Phase 26 substrate).
- `ReleaseDate` is NOT populated by the parser — MAL's `/v2/users/@me/mangalist` exposes `list_status.updated_at` (when the row was last touched by the user) but no per-media publication date in the minimal query shape. Downstream consumers handle the unset sentinel.

## Cross-References

- **Substrate parent**: `src/NzbDrone.Core/ImportLists/OAuthAwareImportListBase.cs` (Plan 27-01 — Trakt-canonical refresh template + D-05 semaphore registry)
- **Settings interface contract**: `src/NzbDrone.Core/ImportLists/IOAuthImportListSettings.cs` (Plan 27-01)
- **Sibling Phase 27 provider (Pattern reference)**: `src/NzbDrone.Core/ImportLists/MangaDex/` (Plan 27-02 — D-08 password-grant) + `src/NzbDrone.Core/ImportLists/AniList/` (Plan 27-03 — D-07 paste-pin). MAL is the most complex of the 3 (D-09 paste-URL + PKCE + state-nonce CSRF + refresh rotation).
- **Pattern source (verbatim shape)**:
  - OAuth Settings POCO: `.planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktSettings.cs:18-46`
  - OAuth proxy: `.planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktProxy.cs`
  - OAuth `RequestAction` surface + RefreshToken null-coalesce: `.planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/Trakt.cs:102-163`
- **Plan**: `.planning/phases/27-3-importlist-provider-plugins-v1-1-inserted-2026-05-17/27-04-PLAN.md`
- **Unit fixture**: `src/NzbDrone.Core.Test/ImportListTests/MyAnimeList/MalImportListFixture.cs` (8 tests — 7 standard + 1 concurrent-refresh proof)
- **LiveService fixture**: `src/NzbDrone.Core.Test/ImportListTests/MyAnimeList/MalRefreshLiveFixture.cs` — `[Category("LiveService")]`; opt-in via `MAL_LIVE_REFRESH_TOKEN` env var. **Paired GH issue: https://github.com/devbrian/Mangarr/issues/219** (label `liveservice-exemption`).
- **Automation fixture**: `src/NzbDrone.Automation.Test/Tests/Settings/ImportLists/MalImportListSettingsFixture.cs` (picker + Edit modal render)
- **FE modal**: `frontend/src/Settings/ImportLists/MyAnimeList/MalCallbackUrlModal.tsx` (paste-URL UI with the 3 reserved `importlist-mal-callback-url-*` testids)
- **Endpoints**:
  - Authorize URL (user opens in new tab): `https://myanimelist.net/v1/oauth2/authorize?response_type=code&client_id=...&code_challenge=...&code_challenge_method=plain&state=...&redirect_uri=https://mangarr.local/oauth/mal/callback`
  - Token exchange: `https://myanimelist.net/v1/oauth2/token` (POST form-urlencoded)
  - Manga list: `https://api.myanimelist.net/v2/users/@me/mangalist` (GET; Bearer-authenticated; paginated)
  - OAuth-app registration: <https://myanimelist.net/apiconfig>
- **API docs**: <https://myanimelist.net/apiconfig/references/authorization> + <https://myanimelist.net/apiconfig/references/api/v2>

## Threats Mitigated

| Threat ID | Mitigation |
|-----------|------------|
| T-27-04-V4 (token leakage to FE) | `[FieldDefinition(Hidden = HiddenType.Hidden, Privacy = PrivacyLevel.Password)]` on AccessToken / RefreshToken / Expires / AuthUser / PendingPkceState (5 fields) — V5 controller strips Hidden fields from outbound schema JSON. |
| T-27-04-V7 (token/code/verifier leakage to logs) | Proxy + provider NEVER log token / code / verifier / refresh-token VALUES. Only endpoint paths + HTTP statuses + exception messages are logged. Plan 27-05 close-out audit grep verifies. |
| T-27-04-V13 (unauthenticated `RequestAction`) | Inherits `[V5ApiController]` Bearer/ApiKey/cookie auth from `ProviderControllerBase` (Phase 26 substrate). |
| T-27-04-V11 CSRF (state nonce forgery) | 32-byte CSPRNG state nonce per OAuth flow; `MalOAuthState.IsValid(presentedState)` enforces ordinal-equal check; rejection returns `{ success: false, error: "InvalidState" }` without contacting MAL. Covered by `state_validation_rejects_mismatched_nonce` test. |
| T-27-04-V11 Replay (code/state reuse) | `Settings.PendingPkceState = null` on first successful exchange (single-use). 10-min TTL via `ExpiresAt` check; rejected with `{ success: false, error: "StateExpired" }`. Covered by `state_validation_rejects_expired_nonce` test. |
| T-27-04-V5 (Tampering — malformed callback URL) | `HttpUtility.ParseQueryString` is null-safe; missing `code` or `state` returns `{ success: false }`; `UriFormatException` caught at top of `RequestAction("getOAuthToken")`. |
| T-27-04-Pitfall-9 (concurrent refresh `400 invalid_grant`) | Inherited from Plan 27-01: per-`Definition.Id` `SemaphoreSlim` + in-lock re-check (peer-flow defense). MAL is the PRIMARY consumer because MAL rotates refresh tokens on every grant call. Covered by `concurrent_refresh_invocations_serialize_via_semaphore` test (8 parallel callers ⇒ exactly 1 RefreshAccessToken proxy call). |
| T-27-04-V6 (predictable state/verifier) | `System.Security.Cryptography.RandomNumberGenerator.GetBytes(N)` (CSPRNG; ASVS V6). NEVER `System.Random` or hash-PRNG. |
| T-27-04-V9 (Transport — bearer over cleartext) | All MAL endpoints HTTPS-only (`myanimelist.net` + `api.myanimelist.net`). `HttpRequestBuilder` with `https://` scheme; no opt-out. |
| T-27-04-V2 (PKCE-plain is weaker than the hashed variant) | MAL constraint (two independent sources confirm). Threat ACCEPTED — MAL's choice, not ours. Future MAL upgrade would be a one-line change in `MalImportListProxy.BuildAuthorizeUrl`. |
| T-27-04-V14 (ClientId compiled-in vs user-supplied) | Mangarr-pinned constant per Sonarr-Trakt precedent. ClientId is NOT a secret in OAuth2 public-client semantics (PKCE replaces the shared-secret defense). |
| T-27-04-V13 LiveService (probe leaks real refresh token in CI) | Opt-in via env var; skipped with Assert.Inconclusive when unset. Paired GH issue #219 tracks cadence; `liveservice-exemption` label. |
| T-27-04-V8 (Information Disclosure — plaintext tokens at rest) | ACCEPT per D-01 — matches Sonarr verbatim; ASVS V8 EXCLUDED per Phase 27 charter. |
