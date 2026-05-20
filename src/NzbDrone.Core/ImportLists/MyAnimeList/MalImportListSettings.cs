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
    //     MangaDex + AniList per-user client models in this same phase. MAL public-client PKCE
    //     flow has NO client_secret (the verifier/challenge replaces the shared-secret defense)
    //     so the Settings POCO carries ClientId but no ClientSecret.
    //   * `PendingPkceState` (index 5) — JSON-serialized MalOAuthState blob held as string,
    //     Hidden. Holds the transient `(StateNonce, Verifier, ExpiresAt)` tuple between
    //     startOAuth and getOAuthToken RequestAction calls (Discretion #2 shape (a)).
    //   * `Status` field at index 1 (D-10 single-select MalListStatus enum, 5 values).
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
            // the FE "Connect" affordance triggers RequestAction("startOAuth"). MAL public-
            // client PKCE flow — user registers the OAuth client at
            // https://myanimelist.net/apiconfig and pastes the issued client_id here. There
            // is NO client_secret (PKCE replaces the shared-secret defense). Mirrors the
            // per-user client model used by MangaDex + AniList in this phase.
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
        // MAL public-client PKCE flow. User registers an OAuth client at
        // https://myanimelist.net/apiconfig and pastes the issued client_id into Mangarr
        // Settings. MAL public-client PKCE has NO client_secret (PKCE replaces the
        // shared-secret defense). Mirrors the per-user client model used by MangaDexImportList
        // + AniListImportList in this phase — Mangarr does NOT register and ship its own
        // upstream MAL OAuth client; each Mangarr install owns its client_id.

        [FieldDefinition(0, Label = "ImportListsMalClientIdLabel", HelpText = "ImportListsMalClientIdHelpText", Type = FieldType.Textbox)]
        public string ClientId { get; set; }

        // ── Single-select per-list status filter (D-10) ──────────────────────────────
        // Sonarr-canonical Trakt user-list pattern: one list per status. Users who want multiple
        // statuses create multiple ImportLists. The FE renders this as a 5-option dropdown driven
        // by the MalListStatus enum members (Reading / PlanToRead / Completed / OnHold / Dropped).

        [FieldDefinition(1, Label = "ImportListsMalStatusLabel", HelpText = "ImportListsMalStatusHelpText", Type = FieldType.Select, SelectOptions = typeof(MalListStatus))]
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

        [FieldDefinition(2, Label = "ImportListsMalAccessTokenLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string AccessToken { get; set; }

        // MAL rotates refresh tokens — 31-day observed lifetime per RESEARCH §STACK §Surface 2.
        // The provider's RefreshToken() override applies the Trakt.cs:151 null-coalesce so the
        // rotated value replaces the previous one (Pitfall 9 — concurrent refresh on the same
        // Definition.Id would otherwise cause `400 invalid_grant` cascade; D-05 SemaphoreSlim
        // serialization from OAuthAwareImportListBase prevents this).
        [FieldDefinition(3, Label = "ImportListsMalRefreshTokenLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string RefreshToken { get; set; }

        [FieldDefinition(4, Label = "ImportListsMalExpiresLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
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

        [FieldDefinition(5, Label = "ImportListsMalPendingPkceStateLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string PendingPkceState { get; set; }

        // ── AuthUser carries the MAL username after first successful exchange ────────
        // MAL's `/v2/users/@me/mangalist` is keyed on the bearer token itself (NOT a
        // username path param), so AuthUser is purely a display-affordance for the
        // Settings UI badge. Populated from the MAL token-response or a follow-up
        // GET /v2/users/@me call — provider picks (RESEARCH §Open Question 5).

        [FieldDefinition(6, Label = "ImportListsMalAuthUserLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string AuthUser { get; set; }

        // ── OAuth sign-in action surface (D-06 / D-09) ───────────────────────────────
        // FieldType.OAuth tells the FE to render a "Connect" button and POST to
        // /api/v5/importlist/action/{SignIn} on click. SignIn string itself is just the action
        // name ("startOAuth") — server-side RequestAction(action) dispatches. The FE then opens
        // the returned OauthUrl in a new tab and renders the MalCallbackUrlModal for paste-back.

        [FieldDefinition(7, Label = "ImportListsMalSignInLabel", HelpText = "ImportListsMalSignInHelpText", Type = FieldType.OAuth)]
        public string SignIn { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
