# ImportLists/MangaDex (Phase 27 Plan 27-02)

## Purpose

MangaDex follows-list ImportList provider plugin. Pulls the user's `/user/follows/manga`
list via personal-client OAuth2 password grant against MangaDex's Keycloak token endpoint,
projects each follow onto an `ImportListItemInfo` row, and feeds it through the Phase 26
substrate's dedup + exclusion + add-manga cascade.

Implements requirement IL-09 (per-user follows-list importer for MangaDex).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\ImportLists\MangaDex`

## Key Files

| File | Purpose |
|------|---------|
| `MangaDexImportList.cs` | Concrete `OAuthAwareImportListBase<MangaDexImportListSettings>` plugin (extends the Plan 27-01 base). Overrides `RequestAction("startOAuth")` (D-08 internal-only password-grant probe) + `RefreshToken()` (Trakt.cs:135-163 verbatim shape with `Settings.RefreshToken = response.RefreshToken ?? Settings.RefreshToken` null-coalesce) + `Fetch()` (D-05 reactive 401-retry decorator on top of base Fetch). Inherits the per-`Definition.Id` `SemaphoreSlim` serialization via the base's `RefreshTokenIfNecessary()` template. |
| `MangaDexImportListSettings.cs` | Settings POCO. Implements `IOAuthImportListSettings` per Plan 27-01 base contract. User-visible credential block (`ClientId`/`ClientSecret`/`Username`/`Password`, indices 0-3 with `Privacy = PrivacyLevel.Password` on `ClientSecret`/`Password`) precedes the hidden token block (`AccessToken`/`RefreshToken`/`Expires`/`AuthUser`, indices 4-7 with `Hidden = HiddenType.Hidden` + `Privacy = PrivacyLevel.Password`). `SignIn` field at index 8 is `FieldType.OAuth` ("Test & Connect" button). |
| `MangaDexImportListProxy.cs` | HTTP proxy with 3 methods: `PasswordGrant` (POST `grant_type=password` to Keycloak token endpoint), `RefreshAccessToken` (POST `grant_type=refresh_token`), `GetFollows` (paginated GET `/user/follows/manga` with `Authorization: Bearer {accessToken}`). Pitfall 10 HARD RULE: every outbound request sets `RateLimitKey = "mangadex"`. T-V7: NEVER logs token values (only endpoint paths + HTTP status). |
| `MangaDexImportListRequestGenerator.cs` | Paginated request chain walking `offset = 0, 100, ... 1000` per `MaxNumResultsPerQuery = 1000` cap (Pitfall 5: MangaDex page size = 100 max). Every chain entry carries `RateLimitKey="mangadex"` + Bearer header + honest `Mangarr/{version}` UA (Phase 1 D-13). |
| `MangaDexImportListParser.cs` | Projects `MangaDexFollowsResource.Data[]` onto `ImportListItemInfo`. Multi-language title resolution: prefer `en`, fall back to first non-empty value. `Links.al` / `Links.mal` promoted to `AniListId` / `MalId` via `int.TryParse` (Pitfall 7 — links values are STRINGS, not ints). |
| `Resource/MangaDexTokenResponse.cs` | Keycloak OIDC token DTO. `[JsonProperty]` snake_case overrides for `access_token` / `refresh_token` / `expires_in` / `refresh_expires_in` / `token_type`. |
| `Resource/MangaDexFollowsResource.cs` | Minimal envelope for GET `/user/follows/manga`: top-level `result` / `response` / `data[]` / `limit` / `offset` / `total`. Sibling shape to `MetadataSource/MangaDex/Resource/MangaResource.cs` (Phase 8 cleanup will collapse the trees). |

## Patterns / Conventions

- **OAuth shape — password grant** (D-08): No browser redirect. User supplies `clientId` / `clientSecret` / `username` / `password` via Settings form; clicks "Test & Connect" which fires `RequestAction("startOAuth")`; the server-side handler POSTs `grant_type=password` to Keycloak; the resulting `access_token` / `refresh_token` round-trip back onto Settings JSON.
- **Refresh-token rotation null-coalesce** (Trakt.cs:151 verbatim): `Settings.RefreshToken = response.RefreshToken ?? Settings.RefreshToken;` future-proofs against Keycloak enabling rotation in a future MangaDex API version (current behavior: refresh_token does NOT rotate; the null-coalesce keeps the current token if the response omits it).
- **SourceKey = `"mangadex"` SHARED** (Pitfall 10 HARD RULE): Every outbound HTTP request carries `RateLimitKey = "mangadex"`, SHARED with `MangaDexMetadataSource` (Phase 2) + `MangaDexIndexer` (Phase 3) + in-process image downloader (Phase 4). Single 40 req/min budget per `MangaDexIndexerSettings.RateSeconds = 1.5`. NO sub-bucket like `"mangadex-importlist"` — would silently double the effective burst.
- **D-01 Sonarr-canonical plaintext token storage**: AccessToken / RefreshToken / Expires / AuthUser stored as plain `string` / `DateTime` columns on the Settings JSON column. Zero crypto-at-rest infrastructure (no IDataProtectionProvider, no DPAPI, no EncryptedSettings BLOB). UI hides the fields via `Hidden = HiddenType.Hidden` + `Privacy = PrivacyLevel.Password`; V5 controller `ProviderControllerBase` strips Hidden fields from outbound schema JSON.
- **D-05 + Pitfall 9 mitigation (inherited from base)**: `OAuthAwareImportListBase.RefreshTokenIfNecessary()` wraps `RefreshToken()` in a per-`Definition.Id` `SemaphoreSlim`. Concurrent `Fetch()` calls collapse to a single live refresh (peer-flow defense).
- **Reactive 401-retry on Fetch**: When the proactive 5-minute lookahead misses (e.g., MangaDex revokes the token outside the window), the overridden `Fetch()` catches `HttpException` with `HttpStatusCode.Unauthorized`, force-expires `Settings.Expires`, calls `RefreshTokenIfNecessary()`, and retries `base.Fetch()` once.
- **Honest UA** (Phase 1 D-13): `User-Agent = $"Mangarr/{BuildInfo.Version.ToString(2)}"` on every outbound request (proxy + request generator both set this explicitly). MangaDex ToS mandate.

## Manga Adaptation Notes

- The follows-list contains the user's complete follow set — NO status filter (per D-10; MangaDex has no `currently-reading` / `plan-to-read` axis at the follows-list level). User-side filtering happens in MangaDex's own UI; Mangarr ingests the whole list.
- Cross-source ID projection: `data[].attributes.links.al` -> `AniListId` and `data[].attributes.links.mal` -> `MalId` via `int.TryParse` (Pitfall 7 — string values). The cross-source resolver in `ImportListSyncService` later promotes partial-ID rows to full `Manga` aggregates.
- `ReleaseDate` is derived from `attributes.year` (best-effort: Jan 1 of the publication year). MangaDex's `/user/follows/manga` doesn't expose a "follow added at" timestamp.

## Cross-References

- **Substrate parent**: `src/NzbDrone.Core/ImportLists/OAuthAwareImportListBase.cs` (Plan 27-01 — Trakt-canonical refresh template + D-05 semaphore registry)
- **Settings interface contract**: `src/NzbDrone.Core/ImportLists/IOAuthImportListSettings.cs` (Plan 27-01)
- **Sibling MangaDex callers (Pitfall 10 — shared SourceKey)**:
  - `src/NzbDrone.Core/MetadataSource/MangaDex/MangaDexMetadataSource.cs` (Phase 2)
  - `src/NzbDrone.Core/Indexers/MangaDex/MangaDexIndexer.cs` (Phase 3)
  - `src/NzbDrone.Core/Download/Clients/InProcess/` image downloader (Phase 4)
- **Pattern source (verbatim shape)**: `.planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/Trakt.cs` + `TraktSettings.cs` + `TraktProxy.cs`
- **Plan**: `.planning/phases/27-3-importlist-provider-plugins-v1-1-inserted-2026-05-17/27-02-PLAN.md`
- **Unit fixture**: `src/NzbDrone.Core.Test/ImportListTests/MangaDex/MangaDexImportListFixture.cs` (4 tests)
- **Automation fixture**: `src/NzbDrone.Automation.Test/Tests/Settings/ImportLists/MangaDexImportListSettingsFixture.cs` (picker + Edit modal render)
- **Endpoints**:
  - Keycloak token: `https://auth.mangadex.org/realms/mangadex/protocol/openid-connect/token`
  - Follows list: `https://api.mangadex.org/user/follows/manga`
  - Personal-client registration: <https://mangadex.org/settings/api-clients>
- **API docs**: <https://api.mangadex.org/docs/02-authentication/personal-clients/>

## Threats Mitigated

| Threat ID | Mitigation |
|-----------|------------|
| T-27-02-V4 (token leakage to FE) | `[FieldDefinition(Hidden = HiddenType.Hidden, Privacy = PrivacyLevel.Password)]` on AccessToken/RefreshToken/Expires/AuthUser fields — V5 controller strips Hidden fields from outbound schema JSON. |
| T-27-02-V7 (token leakage to logs) | Proxy + provider NEVER log token values. Only `_logger.Trace("Refreshing Token")` (Sonarr-canonical) and endpoint paths / HTTP statuses. |
| T-27-02-V13 (unauthenticated `RequestAction`) | Inherits `[V5ApiController]` Bearer/ApiKey/cookie auth from `ProviderControllerBase` (Phase 26 substrate). |
| T-27-02-Pitfall-10 (SourceKey fragmentation) | HARD RULE: every outbound request sets `RateLimitKey = "mangadex"` SHARED with all sibling MangaDex callers. Verified by audit grep at Plan 27-05 close-out. |
| T-27-02-Pitfall-9 (concurrent-refresh `400 invalid_grant`) | Inherited from `OAuthAwareImportListBase` — per-`Definition.Id` `SemaphoreSlim` + peer-flow re-check. |
