using System;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    // Phase 27 Plan 27-04 (D-01, D-09, D-10) — MAL list provider Settings POCO.
    //
    // Pattern source (composite):
    //   * Trakt-verbatim shape: .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktSettings.cs:18-46
    //   * Sibling-canonical Phase 27 shape: src/NzbDrone.Core/ImportLists/AniList/AniListImportListSettings.cs
    //
    // Phase 27 augments vs Trakt verbatim:
    //   * `Privacy = PrivacyLevel.Password` alongside `Hidden = HiddenType.Hidden` on every
    //     token-bearing field (T-27-04-V4 mitigation; Phase 27 ImportList carries this for
    //     defense-in-depth on the V5 controller's outbound JSON redaction path).
    //   * Inherit `ImportListSettingsBase<MalImportListSettings>` (Phase 26 substrate) NOT
    //     `NotificationSettingsBase<TSettings>`.
    //   * Implement `IOAuthImportListSettings` so `OAuthAwareImportListBase<TSettings>` can drive
    //     `RefreshTokenIfNecessary()` generically (Plan 27-01 base contract).
    //
    // Phase 27 deltas vs sibling AniList provider (Plan 27-03):
    //   * MAL ClientId is user-supplied via `Settings.ClientId` (index 0) — consistent with
    //     MangaDex + AniList per-user client models in this same phase.
    //   * MAL supports two App Types at https://myanimelist.net/apiconfig:
    //       - "Other" (public-client PKCE): NO client_secret — the verifier/challenge replaces
    //         the shared-secret defense. Mangarr's original v1.1 ship assumed this app type.
    //       - "web" (confidential-client PKCE): REQUIRES client_secret in the token-exchange
    //         and refresh-token form bodies alongside the PKCE verifier per MAL blog
    //         https://myanimelist.net/blog.php?eid=835707. This is the default app type on
    //         registration — most users hit this path. GH #233 (2026-05-21) surfaced the
    //         missing-secret 401 on "web" app types.
    //   * Therefore `Settings.ClientSecret` is rendered as an OPTIONAL user-visible Password
    //     field at index 1. The proxy methods accept it as a string parameter and append it
    //     conditionally (only when non-empty) — back-compat with the "Other" app-type flow.
    //   * `PendingPkceState` (index 6) — JSON-serialized MalOAuthState blob held as string,
    //     Hidden. Holds the transient `(StateNonce, Verifier, ExpiresAt)` tuple between
    //     startOAuth and getOAuthToken RequestAction calls (Discretion #2 shape (a)).
    //   * `Status` field at index 2 (D-10 single-select MalListStatus enum, 5 values).
    //   * MAL DOES rotate refresh tokens (31-day observed lifetime per RESEARCH §STACK §Surface 2),
    //     so the RefreshToken override applies the Trakt.cs:151 null-coalesce; the field is
    //     LIVE on MAL (unlike AniList where it's contract-compliance dead weight).
    //
    // D-01 enforcement: tokens stored as plain string / DateTime — zero crypto-at-rest infrastructure
    // of any shape (no key-protection provider, no platform secret-store bridge, no per-row sealed blob).
    // ASVS V8 EXPLICITLY EXCLUDED per Phase 27 charter; matches Sonarr verbatim. See
    // IOAuthImportListSettings.cs for the contract-level audit signal.
    //
    // D-09 enforcement: `SignIn = "startOAuth"` ctor pattern. V5 FE's FieldType.OAuth renderer wires
    // the click handler to POST /api/v5/importlist/action/MyAnimeList with body
    // `{ name: "startOAuth", query: {} }`; the provider's RequestAction("startOAuth") generates the
    // PKCE flow state, persists it on PendingPkceState, and returns the MAL authorize URL the
    // MalCallbackUrlModal opens in a new tab. After the user pastes the redirected URL (containing
    // `?code=X&state=Y`) into the modal, a second RequestAction("getOAuthToken", { redirectedUrl })
    // validates state, exchanges the code+verifier for tokens, and clears PendingPkceState.
    public class MalImportListSettingsValidator : AbstractValidator<MalImportListSettings>
    {
        public MalImportListSettingsValidator()
        {
            // Initial-save guardrails: user-supplied OAuth client_id MUST be present before
            // the FE "Connect" affordance triggers RequestAction("startOAuth"). User registers
            // the OAuth client at https://myanimelist.net/apiconfig and pastes the issued
            // client_id here. Mirrors the per-user client model used by MangaDex + AniList in
            // this phase.
            //
            // ClientSecret is OPTIONAL by design (NO .NotEmpty() rule): MAL App Type "Other"
            // (public-client PKCE) does not issue / require a client_secret. Only MAL App Type
            // "web" (confidential-client PKCE) requires it. Adding NotEmpty here would force
            // every existing "Other" app-type user to invent a non-empty value, which would in
            // turn break their PKCE-only token exchange (MAL rejects unknown client_secret with
            // 401 invalid_client).
            RuleFor(c => c.ClientId).NotEmpty();

            // Status MUST be set before Connect. Sonarr-canonical single-select Trakt
            // user-list pattern (one list per status). Reading is the default in the ctor —
            // IsInEnum suffices to reject corrupted serialized values; NotEmpty would
            // always pass for enum-typed fields.
            RuleFor(c => c.Status).IsInEnum();

            // Post-exchange guardrails: after a successful PKCE code-exchange the token block
            // must round-trip into Settings JSON. The FE Save handler invokes Validate() on
            // every PUT — these rules ensure the user cannot persist a half-exchanged state.
            // MAL DOES rotate refresh tokens (unlike AniList), so RefreshToken is part of the
            // post-exchange invariant.
            RuleFor(c => c.AccessToken)
                .NotEmpty()
                .When(c => c.Expires != default(DateTime));

            RuleFor(c => c.RefreshToken)
                .NotEmpty()
                .When(c => c.Expires != default(DateTime));
        }
    }

    public class MalImportListSettings : ImportListSettingsBase<MalImportListSettings>, IOAuthImportListSettings
    {
        private static readonly MalImportListSettingsValidator Validator = new();

        public MalImportListSettings()
        {
            // BaseUrl is fixed at the MAL v2 API root — MAL doesn't ship self-hosted
            // instances, so we hard-code the canonical host and surface no [FieldDefinition]
            // (T-CONFIG-DRIFT-01 mitigation by absence, mirrors AniListImportListSettings.cs:78).
            BaseUrl = MalConstants.ApiBaseUrl;

            // D-06 / D-09 Sonarr-canonical OAuth action surface — mirrors TraktSettings.cs:24
            // verbatim. The FE FieldType.OAuth control reads this string and routes the click to
            // POST /api/v5/importlist/action/{SignIn}.
            SignIn = "startOAuth";

            // Sensible default — most users initially want to import their "currently reading"
            // shelf. Per D-10 the user can change this to any of the 5 MalListStatus values.
            Status = MalListStatus.Reading;
        }

        // IImportListSettings contract requirement (declared abstract on
        // ImportListSettingsBase<TSettings>). MAL doesn't expose this in the UI —
        // fixed at the api.myanimelist.net host per ToS.
        public override string BaseUrl { get; set; }

        // ── User-supplied OAuth client identifier (D-09 per-user model) ───────────────
        // MAL OAuth flow. User registers an OAuth client at
        // https://myanimelist.net/apiconfig and pastes the issued client_id into Mangarr
        // Settings. Mangarr does NOT register and ship its own upstream MAL OAuth client;
        // each Mangarr install owns its client_id (mirrors MangaDex + AniList per-user
        // client shapes in this phase).
        //
        // Whether ClientSecret is required depends on the MAL App Type:
        //   * App Type "Other": pure PKCE, NO client_secret required.
        //   * App Type "web":   confidential PKCE, REQUIRES client_secret in token-exchange
        //                       + refresh-token form bodies (GH #233).

        [FieldDefinition(0, Label = "ImportListsMalClientIdLabel", HelpText = "ImportListsMalClientIdHelpText", Type = FieldType.Textbox)]
        public string ClientId { get; set; }

        // ── Optional OAuth client secret (GH #233 — MAL App Type "web" support) ──────
        // MAL App Type "web" issues a client_secret alongside the client_id and REQUIRES
        // it in the token-exchange + refresh-token form bodies per MAL's OAuth docs
        // (https://myanimelist.net/blog.php?eid=835707). MAL App Type "Other" does NOT
        // issue a client_secret and does NOT require one in any request body.
        //
        // Mangarr surfaces this as an OPTIONAL Password-typed input — when set, the proxy
        // appends `client_secret={value}` to the token-exchange + refresh form bodies; when
        // empty, the parameter is omitted entirely (back-compat with the original "Other"
        // app-type ship). The validator carries NO NotEmpty rule on this field.
        //
        // Privacy = PrivacyLevel.Password is correct here because this is a USER-VISIBLE
        // input field (NOT a hidden token block). The FE renders it as `<input type=password>`
        // so over-the-shoulder readers can't see the value, and the V5 controller's outbound
        // JSON redaction kicks in (T-27-04-V4 defense-in-depth). The comment below on the
        // Hidden token block notes a separate constraint: Privacy=Password is unsafe on
        // Hidden token fields because SchemaBuilder's `********` placeholder breaks the
        // new-Add round-trip there — that constraint applies only to Hidden=Hidden fields,
        // NOT to user-visible Password fields like this one.

        [FieldDefinition(1, Label = "ImportListsMalClientSecretLabel", HelpText = "ImportListsMalClientSecretHelpText", Type = FieldType.Textbox, Privacy = PrivacyLevel.Password)]
        public string ClientSecret { get; set; }

        // ── Single-select per-list status filter (D-10) ──────────────────────────────
        // Sonarr-canonical Trakt user-list pattern: one list per status. Users who want multiple
        // statuses create multiple ImportLists. The FE renders this as a 5-option dropdown driven
        // by the MalListStatus enum members (Reading / PlanToRead / Completed / OnHold / Dropped).

        [FieldDefinition(2, Label = "ImportListsMalStatusLabel", HelpText = "ImportListsMalStatusHelpText", Type = FieldType.Select, SelectOptions = typeof(MalListStatus))]
        public MalListStatus Status { get; set; }

        // ── Hidden OAuth token block (D-01 + T-27-04-V4 mitigation) ──────────────────
        // Hidden = HiddenType.Hidden suppresses UI rendering. Sonarr-canonical
        // TraktSettings pattern uses ONLY the Hidden flag on token-bearing fields
        // (no Privacy = PrivacyLevel.Password). Adding Privacy=Password breaks the
        // round-trip for new-add flows: SchemaBuilder.ReadFromSchema's "keep old
        // value" branch is gated on `model != null`, which is false for new
        // ImportList add — `********` falls through to the setter and STJUtcConverter
        // throws FormatException on `DateTime.Parse("********")`. Matches Sonarr
        // TraktSettings.cs:27-37 verbatim.

        [FieldDefinition(3, Label = "ImportListsMalAccessTokenLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string AccessToken { get; set; }

        // MAL rotates refresh tokens — 31-day observed lifetime per RESEARCH §STACK §Surface 2.
        // The provider's RefreshToken() override applies the Trakt.cs:151 null-coalesce so the
        // rotated value replaces the previous one (Pitfall 9 — concurrent refresh on the same
        // Definition.Id would otherwise cause `400 invalid_grant` cascade; D-05 SemaphoreSlim
        // serialization from OAuthAwareImportListBase prevents this).
        [FieldDefinition(4, Label = "ImportListsMalRefreshTokenLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string RefreshToken { get; set; }

        [FieldDefinition(5, Label = "ImportListsMalExpiresLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public DateTime Expires { get; set; }

        // ── Transient PKCE flow state (Discretion #2 shape (a)) ──────────────────────
        // Holds a JSON-serialized MalOAuthState { StateNonce, Verifier, ExpiresAt } between
        // startOAuth and getOAuthToken RequestAction calls. Cleared (set to null) on first
        // successful exchange (single-use enforcement; T-V11 mitigation).
        //
        // Storage shape: string-typed blob (NOT typed MalOAuthState) so the substrate's
        // generic Settings-JSON-column persistence handles it without per-provider schema
        // changes. The provider class serializes via JsonConvert.SerializeObject(state) on
        // write and JsonConvert.DeserializeObject<MalOAuthState>(blob) on read.

        [FieldDefinition(6, Label = "ImportListsMalPendingPkceStateLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string PendingPkceState { get; set; }

        // ── AuthUser carries the MAL username after first successful exchange ────────
        // MAL's `/v2/users/@me/mangalist` is keyed on the bearer token itself (NOT a
        // username path param), so AuthUser is purely a display-affordance for the
        // Settings UI badge. Populated from the MAL token-response or a follow-up
        // GET /v2/users/@me call — provider picks (RESEARCH §Open Question 5).

        [FieldDefinition(7, Label = "ImportListsMalAuthUserLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string AuthUser { get; set; }

        // ── OAuth sign-in action surface (D-06 / D-09) ───────────────────────────────
        // FieldType.OAuth tells the FE to render a "Connect" button and POST to
        // /api/v5/importlist/action/{SignIn} on click. SignIn string itself is just the action
        // name ("startOAuth") — server-side RequestAction(action) dispatches. The FE then opens
        // the returned OauthUrl in a new tab and renders the MalCallbackUrlModal for paste-back.

        [FieldDefinition(8, Label = "ImportListsMalSignInLabel", HelpText = "ImportListsMalSignInHelpText", Type = FieldType.OAuth)]
        public string SignIn { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
