# ImportLists/AniList (Phase 27 Plan 27-03)

## Purpose

AniList list ImportList provider plugin. Pulls the user's AniList manga list
(filtered to a single `MediaListStatus` per ImportList per D-10) via the AniList
public GraphQL API, projects each list entry onto an `ImportListItemInfo` row, and
feeds it through the Phase 26 substrate's dedup + exclusion + add-manga cascade.

Implements requirement IL-10 (per-user AniList list importer).

**Path**: `src/NzbDrone.Core/ImportLists/AniList/`

## Key Files

| File | Purpose |
|------|---------|
| `AniListImportList.cs` | Concrete `OAuthAwareImportListBase<AniListImportListSettings>` plugin (extends the Plan 27-01 base). Overrides `RequestAction` for the D-07 paste-pin paste-back flow (`startOAuth` → returns pin authorize URL; `getAuthPin` → exchanges the pasted pin for the 1-year JWT via the proxy and persists tokens on Settings). `RefreshToken()` override is a no-op that logs a warning (AniList issues 1-year JWTs with no refresh-token grant; re-auth is user-driven). `FetchImportListResponse` override routes the AniList GraphQL endpoint through the SHARED `IAniListGraphQlTransport` (Phase 26 Plan 26-02) instead of `_httpClient.Execute` — preserves the transport's `RateLimitKey="anilist"` budget honoring without a parallel transport class. |
| `AniListListStatus.cs` | D-10 single-select enum (6 values: `CURRENT`/`PLANNING`/`COMPLETED`/`PAUSED`/`DROPPED`/`REPEATING`). UPPERCASE matches AniList GraphQL `MediaListStatus` string projections verbatim. Drives the `FieldType.Select` dropdown on the Settings form. |
| `AniListImportListSettings.cs` | Settings POCO. Implements `IOAuthImportListSettings` per Plan 27-01 base contract. User-visible fields: `ClientId`/`ClientSecret` (indices 0-1 with `Privacy=PrivacyLevel.Password` on `ClientSecret`) + `Status` enum (index 2, `FieldType.Select`). Hidden token block: `AccessToken`/`RefreshToken`/`Expires`/`AuthUser` (indices 3-6 with `Hidden=HiddenType.Hidden` only — Sonarr-canonical TraktSettings pattern; `Privacy = PrivacyLevel.Password` was removed from Hidden fields in Phase 27 close-out fix-forward). `SignIn` at index 7 (`FieldType.OAuth`, "Connect" button). `RefreshToken` field present on POCO purely for `IOAuthImportListSettings` contract compliance — never populated by AniList (1-year JWT, no refresh-token grant). |
| `AniListImportListProxy.cs` | OAuth pin-exchange HTTP proxy with 2 methods: `GetPinAuthorizeUrl` (deterministic URL construction; no HTTP call) + `ExchangePinForToken` (POST form-urlencoded `grant_type=authorization_code` to `https://anilist.co/api/v2/oauth/token`). Honest `Mangarr/{version}` UA + `Accept: application/json`. NO `RateLimitKey` set — pin/token exchange is a fire-once-per-year auth flow outside the `SourceKey="anilist"` GraphQL budget. T-V7: NEVER logs the pin, ClientSecret, or access-token value. |
| `AniListImportListRequestGenerator.cs` | Single-page `ImportListPageableRequestChain` (AniList's `MediaListCollection` returns the entire list in one response — no pagination per status). Builds the GraphQL body via `Json.ToJson(new { query, variables })`; `variables.status = Settings.Status.ToString().ToUpperInvariant()` per D-10 propagation. Query string is a `const` literal (T-INJ-03 anti-injection invariant). |
| `AniListImportListParser.cs` | Walks `MediaListCollection.lists[].entries[].media` projecting each to `ImportListItemInfo { Title, AniListId, MalId }`. Title preference: English first, fall back to Romaji. MalId promoted only when non-null AND > 0. Null-safe at every nesting level. |
| `Resource/AniListTokenResponse.cs` | OAuth2 token-exchange DTO. `[JsonProperty]` snake_case for `token_type` / `expires_in` / `access_token`. NO `refresh_token` field — AniList omits per 1-year JWT shape. |
| `Resource/AniListMediaListResource.cs` | DTO for the `MediaListCollection` GraphQL response. Minimal projection: `MediaListCollection { lists[] { entries[] { media { id, idMal, title { romaji, english } } } } }`. Uses project-default Newtonsoft camelCase contract. Re-uses the SHARED `AniListGraphQlResponse<T>` wrapper from `MetadataSource/AniList/Resource/AniListGraphQlResponse.cs`. |

## Patterns / Conventions

- **OAuth shape — Auth Pin paste-back** (D-07): No browser redirect. User registers an OAuth application at <https://anilist.co/settings/developer>; pastes the issued `ClientId` + `ClientSecret` into Mangarr Settings; clicks "Connect" which fires `RequestAction("startOAuth")` and opens `https://anilist.co/api/v2/oauth/pin?client_id=...&response_type=code` in a new tab. The user reads the pin off the AniList page, switches back to Mangarr, and pastes it into `AniListPinModal` which dispatches `RequestAction("getAuthPin", { pin })`. The server exchanges the pin for a 1-year JWT, runs a follow-up `Viewer { name }` GraphQL query to resolve `Settings.AuthUser`, and returns the public envelope `{ accessToken, expires, authUser }`.
- **NO refresh-token grant**: AniList issues 1-year JWT access tokens with no refresh endpoint. The `RefreshToken()` override is a no-op that logs `"AniList tokens are 1-year JWTs; refresh not supported. Re-authenticate via Settings → ImportLists when the current token expires."`. On 401 from AniList (token actually invalidated upstream) the inherited `HttpImportListBase` exception ladder records `RecordFailure` and surfaces the actionable "Re-authenticate" error to the FE banner; the user re-runs the pin flow from Settings.
- **D-10 single-select Status enum**: `[FieldDefinition(Type = FieldType.Select, SelectOptions = typeof(AniListListStatus))]` renders as a 6-option dropdown. Users who want multiple statuses create multiple ImportLists (one per status). Multi-select is a v1.2+ enhancement per CONTEXT line 442.
- **Shared transport reuse (NO parallel transport)**: `AniListImportList.FetchImportListResponse` detects the `https://graphql.anilist.co` endpoint on outbound requests and routes through `IAniListGraphQlTransport.Post<...>` (Phase 26 Plan 26-02 extraction at `src/NzbDrone.Core/MetadataSource/AniList/AniListGraphQlTransport.cs`). The transport already sets `RateLimitKey="anilist"` at line 50 — single 30 req/min budget bucket SHARED with `AniListMetadataSource`. NO parallel transport implementation class at the ImportList tier — verified by audit-grep at Plan 27-05 close-out.
- **`SourceKey="anilist"` NEW bucket** (Pitfall 4): AniList does NOT share a SourceKey with any other Mangarr caller (unlike MangaDex which shares across MetadataSource/Indexer/Downloader). The bucket is fresh per Phase 27 charter; 30 req/min default ceiling.
- **D-01 Sonarr-canonical plaintext token storage**: AccessToken / Expires / AuthUser stored as plain `string` / `DateTime` on the Settings JSON column. Zero crypto-at-rest infrastructure (no key-protection provider, no platform secret-store bridge, no per-row sealed blob). UI hides the fields via `Hidden = HiddenType.Hidden` only (Sonarr-canonical TraktSettings.cs:27-37); V5 controller `ProviderControllerBase` strips Hidden fields from outbound schema JSON.
- **D-05 + Pitfall 9 mitigation (inherited from base)**: `OAuthAwareImportListBase.RefreshTokenIfNecessary()` wraps `RefreshToken()` in a per-`Definition.Id` `SemaphoreSlim`. For AniList the wrapper is effectively dormant (RefreshToken is a no-op) but the structural shape is consistent across MangaDex/AniList/MAL providers — Pitfall 9 doesn't actually materialize for AniList because the noop has no rotation race to defend against.
- **Anti-injection invariant** (T-INJ-03 + sibling `AniListMangaApi.cs:23`): the GraphQL query string in `AniListImportListRequestGenerator` is a `const string` literal; ALL user-supplied values (Settings.AuthUser, Settings.Status) flow through the `variables` object so the GraphQL parser receives them as typed parameters, never as inline interpolation.
- **Honest UA** (Phase 1 D-13): `User-Agent = $"Mangarr/{BuildInfo.Version.ToString(2)}"` on every outbound request (proxy + request generator both set this explicitly). AniList docs encourage honest UA disclosure.

## Manga Adaptation Notes

- The list is filtered to a single `MediaListStatus` per ImportList per D-10 — unlike MangaDex's complete-follow-set retrieval. Users segregate their currently-reading vs plan-to-read vs completed lists by creating multiple ImportLists.
- Cross-source ID projection: AniList's `media.idMal` is promoted to `ImportListItemInfo.MalId` directly (typed `int?` from AniList; no string-parsing required unlike MangaDex's stringly-typed `links.mal` field). The cross-source resolver in `ImportListSyncService` later promotes partial-ID rows (AniListId + optional MalId, no MangaDexId) to full `Manga` aggregates by querying MangaDex with the resolved AniList ID via `SearchForNewMangaByAniListId`.
- `ReleaseDate` is NOT populated by the parser — AniList's `MediaListCollection` doesn't expose a list-add timestamp or a per-media publication date in the minimal query shape. Downstream consumers handle the unset sentinel.

## Cross-References

- **Substrate parent**: `src/NzbDrone.Core/ImportLists/OAuthAwareImportListBase.cs` (Plan 27-01 — Trakt-canonical refresh template + D-05 semaphore registry)
- **Settings interface contract**: `src/NzbDrone.Core/ImportLists/IOAuthImportListSettings.cs` (Plan 27-01)
- **Sibling Phase 27 provider (Pattern reference)**: `src/NzbDrone.Core/ImportLists/MangaDex/` (Plan 27-02 — first concrete `OAuthAwareImportListBase` consumer; same registration shape + Settings POCO structure with provider-specific OAuth flow swap)
- **Shared GraphQL transport**: `src/NzbDrone.Core/MetadataSource/AniList/AniListGraphQlTransport.cs` (Phase 26 Plan 26-02 extraction). Sibling consumer: `src/NzbDrone.Core/MetadataSource/AniList/AniListMetadataSource.cs`.
- **Shared response envelope**: `src/NzbDrone.Core/MetadataSource/AniList/Resource/AniListGraphQlResponse.cs` (Phase 2). Re-used by `AniListImportListParser` — no duplicate envelope at the ImportList tier.
- **Pattern source (verbatim shape)**:
  - OAuth Settings POCO: `.planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktSettings.cs:18-46`
  - OAuth proxy: `.planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktProxy.cs`
  - OAuth `RequestAction` surface: `.planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/Trakt.cs:102-125`
- **Plan**: `.planning/phases/27-3-importlist-provider-plugins-v1-1-inserted-2026-05-17/27-03-PLAN.md`
- **Unit fixture**: `src/NzbDrone.Core.Test/ImportListTests/AniList/AniListImportListFixture.cs` (6 tests)
- **Automation fixture**: `src/NzbDrone.Automation.Test/Tests/Settings/ImportLists/AniListImportListSettingsFixture.cs` (picker + Edit modal render)
- **FE modal**: `frontend/src/Settings/ImportLists/AniList/AniListPinModal.tsx` (paste-back UI with the 3 reserved `importlist-anilist-pin-*` testids)
- **Endpoints**:
  - Pin authorize URL (user opens in new tab): `https://anilist.co/api/v2/oauth/pin?client_id={id}&response_type=code`
  - Token exchange: `https://anilist.co/api/v2/oauth/token` (POST form-urlencoded)
  - GraphQL: `https://graphql.anilist.co` (routed through shared transport)
  - OAuth-app registration: <https://anilist.co/settings/developer>
- **API docs**: <https://docs.anilist.co/guide/auth/>

## Threats Mitigated

| Threat ID | Mitigation |
|-----------|------------|
| T-27-03-V4 (token leakage to FE) | `[FieldDefinition(Hidden = HiddenType.Hidden)]` on AccessToken/RefreshToken/Expires/AuthUser fields (Sonarr-canonical TraktSettings pattern). V5 controller strips Hidden fields from outbound schema JSON. |
| T-27-03-V7 (secret leakage to logs) | Proxy + provider NEVER log token / pin / ClientSecret VALUES. Only endpoint paths + HTTP statuses + exception messages are logged. Plan 27-05 close-out audit verifies. |
| T-27-03-V13 (unauthenticated `RequestAction`) | Inherits `[V5ApiController]` Bearer/ApiKey/cookie auth from `ProviderControllerBase` (Phase 26 substrate). |
| T-27-03-V5 (Tampering — malformed GraphQL response) | Parser projects only documented fields with null-checks at every nesting level (data/MediaListCollection/lists/entries/media/title). Unknown fields ignored by Newtonsoft default. Empty list arrays handled (returns empty IList). |
| T-27-03-V9 (Transport — bearer over cleartext) | All AniList endpoints HTTPS-only (anilist.co + graphql.anilist.co). `HttpRequestBuilder` with `https://` scheme; no opt-out. |
| T-27-03-V11 (Replay — pin reuse) | AniList enforces single-use server-side (we trust the upstream). On exchange failure the provider returns `{ success: false }` with NO retry — user must request a fresh pin. |
| T-27-03-V2 (Authentication — 1-year JWT silently expires) | `RefreshToken` is a no-op by design. On 401 the inherited HTTP ladder records `RecordFailure` and the FE surfaces "Re-authenticate" actionable error. Per-list status banner already shipped Phase 26 substrate. |
| T-27-03-V8 (Information Disclosure — plaintext tokens at rest) | ACCEPT per D-01 — matches Sonarr verbatim; ASVS V8 EXCLUDED per Phase 27 charter. |
