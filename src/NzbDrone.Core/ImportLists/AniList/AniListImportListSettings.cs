using System;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.AniList
{
    // Phase 27 Plan 27-03 (D-01, D-07, D-10) — AniList list provider Settings POCO.
    //
    // Pattern source (composite):
    //   * Trakt-verbatim shape: .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktSettings.cs:18-46
    //   * Sibling-canonical Phase 27 shape: src/NzbDrone.Core/ImportLists/MangaDex/MangaDexImportListSettings.cs
    //
    // Phase 27 augments vs Trakt verbatim:
    //   * `Hidden = HiddenType.Hidden` on every token-bearing field (T-27-03-V4 mitigation).
    //     The V5 controller's `ProviderControllerBase` strips Hidden fields from outbound
    //     schema JSON. NO `Privacy = PrivacyLevel.Password` on those fields — that combo was
    //     tried in early Phase 27 but broke the new-Add round-trip per SchemaBuilder.cs
    //     fall-through path (FormatException on `DateTime.Parse("********")`). Matches
    //     Sonarr-canonical TraktSettings.cs:27-37 verbatim.
    //   * Inherit `ImportListSettingsBase<AniListImportListSettings>` (Phase 26 substrate) NOT
    //     `NotificationSettingsBase<TSettings>`.
    //   * Implement `IOAuthImportListSettings` so `OAuthAwareImportListBase<TSettings>` can drive
    //     `RefreshTokenIfNecessary()` generically (Plan 27-01 base contract).
    //
    // Phase 27 deltas vs sibling MangaDex provider (Plan 27-02):
    //   * AniList registers an OAuth client via https://anilist.co/settings/developer; user pastes
    //     the issued ClientId + ClientSecret here. No Mangarr-pinned client (per D-07 — user-owned
    //     OAuth app per AniList ToS).
    //   * NO Username/Password fields — AniList's Auth Pin flow (D-07) replaces password-grant.
    //   * `Status` field at index 2 (D-10 single-select MediaListStatus enum, 6 values).
    //   * `RefreshToken` field present per `IOAuthImportListSettings` contract but UNUSED by AniList
    //     (1-year JWT; no refresh-token grant per RESEARCH §STACK §Surface 2). The provider's
    //     `RefreshToken()` override is a no-op (logs a warning).
    //
    // D-01 enforcement: tokens stored as plain string / DateTime — zero crypto-at-rest infrastructure
    // of any shape (no key-protection provider, no platform secret-store bridge, no per-row sealed blob).
    // ASVS V8 EXPLICITLY EXCLUDED per Phase 27 charter; matches Sonarr verbatim. See
    // IOAuthImportListSettings.cs for the contract-level audit signal.
    //
    // D-07 enforcement: `SignIn = "startOAuth"` ctor pattern. V5 FE's FieldType.OAuth renderer wires
    // the click handler to POST /api/v5/importlist/action/AniList with body
    // `{ name: "startOAuth", query: {} }`; the provider returns the pin authorize URL the AniListPinModal
    // opens in a new tab. After the user pastes the pin into the modal, a second action
    // `getAuthPin` exchanges the pin for the access token (see AniListImportList.RequestAction).
    public class AniListImportListSettingsValidator : AbstractValidator<AniListImportListSettings>
    {
        public AniListImportListSettingsValidator()
        {
            // BaseUrl pinned to the canonical AniList GraphQL endpoint. The ctor sets the
            // default; this validator rejects any API payload that mutates it to a
            // non-canonical host (defense against config-drift / accidental override).
            RuleFor(c => c.BaseUrl).Equal("https://graphql.anilist.co");

            // Initial-save guardrails: user-supplied OAuth-client credentials MUST be present
            // before the FE "Connect" affordance triggers RequestAction("startOAuth"). Status
            // is also required for an initial save — Sonarr-canonical single-select Trakt
            // user-list pattern (one list per status).
            RuleFor(c => c.ClientId).NotEmpty();
            RuleFor(c => c.ClientSecret).NotEmpty();

            // Post-exchange guardrails: after a successful pin→token exchange the AccessToken +
            // AuthUser must round-trip into Settings JSON. The FE Save handler invokes Validate()
            // on every PUT — these rules ensure the user cannot persist a half-exchanged state.
            // Mirrors Trakt's TraktSettingsValidator (TraktSettings.cs:10-15) shape but adapted
            // for 1-year JWT (no refresh-token branch — AniList omits refresh_token).
            RuleFor(c => c.AccessToken)
                .NotEmpty()
                .When(c => c.Expires != default(DateTime));

            RuleFor(c => c.AuthUser)
                .NotEmpty()
                .When(c => c.Expires != default(DateTime));
        }
    }

    public class AniListImportListSettings : ImportListSettingsBase<AniListImportListSettings>, IOAuthImportListSettings
    {
        private static readonly AniListImportListSettingsValidator Validator = new();

        public AniListImportListSettings()
        {
            // BaseUrl is fixed at AniList's GraphQL endpoint — AniList doesn't ship self-hosted
            // instances, so we hard-code the canonical host and surface no [FieldDefinition]
            // (T-CONFIG-DRIFT-01 mitigation by absence, mirrors MangaDexImportListSettings.cs:76).
            BaseUrl = "https://graphql.anilist.co";

            // D-06 / D-07 Sonarr-canonical OAuth action surface — mirrors TraktSettings.cs:24
            // verbatim. The FE FieldType.OAuth control reads this string and routes the click to
            // POST /api/v5/importlist/action/{SignIn}.
            SignIn = "startOAuth";

            // Sensible default — most users initially want to import their "currently reading"
            // shelf. Per D-10 the user can change this to any of the 6 MediaListStatus values.
            Status = AniListListStatus.CURRENT;
        }

        // IImportListSettings contract requirement (declared abstract on
        // ImportListSettingsBase<TSettings>). AniList doesn't expose this in the UI
        // — fixed at the graphql.anilist.co host per ToS.
        public override string BaseUrl { get; set; }

        // ── User-visible OAuth-client credential block (D-07) ────────────────────────
        // AniList personal-client OAuth requires user-supplied client_id + client_secret because
        // AniList does not host centralized Mangarr-pinned credentials. Users register an OAuth
        // application at https://anilist.co/settings/developer and paste the issued credentials
        // here. Sibling-canonical with Plan 27-02 MangaDex Settings (which uses the same
        // user-owned-credentials shape per MangaDex personal-client OAuth tier).

        [FieldDefinition(0, Label = "ImportListsAniListClientIdLabel", HelpText = "ImportListsAniListClientIdHelpText", Type = FieldType.Textbox)]
        public string ClientId { get; set; }

        [FieldDefinition(1, Label = "ImportListsAniListClientSecretLabel", HelpText = "ImportListsAniListClientSecretHelpText", Type = FieldType.Textbox, Privacy = PrivacyLevel.Password)]
        public string ClientSecret { get; set; }

        // ── Single-select per-list status filter (D-10) ──────────────────────────────
        // Sonarr-canonical Trakt user-list pattern: one list per status. Users who want multiple
        // statuses create multiple ImportLists. The FE renders this as a 6-option dropdown driven
        // by the AniListListStatus enum members (CURRENT/PLANNING/COMPLETED/PAUSED/DROPPED/REPEATING).

        [FieldDefinition(2, Label = "ImportListsAniListStatusLabel", HelpText = "ImportListsAniListStatusHelpText", Type = FieldType.Select, SelectOptions = typeof(AniListListStatus))]
        public AniListListStatus Status { get; set; }

        // ── Hidden OAuth token block (D-01 + T-27-03-V4 mitigation) ──────────────────
        // Hidden = HiddenType.Hidden suppresses UI rendering. Sonarr-canonical
        // TraktSettings pattern uses ONLY the Hidden flag on token-bearing fields
        // (no Privacy = PrivacyLevel.Password). Adding Privacy=Password breaks the
        // round-trip for new-add flows: SchemaBuilder.ReadFromSchema's "keep old
        // value" branch is gated on `model != null`, which is false for new
        // ImportList add — `********` falls through to the setter and STJUtcConverter
        // throws FormatException on `DateTime.Parse("********")`. Matches Sonarr
        // TraktSettings.cs:27-37 verbatim.

        [FieldDefinition(3, Label = "ImportListsAniListAccessTokenLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string AccessToken { get; set; }

        // AniList does NOT issue refresh tokens (1-year JWT lifetime per
        // RESEARCH §STACK §Surface 2). The field is carried on the POCO solely to satisfy the
        // IOAuthImportListSettings interface contract (Plan 27-01 — shared base class needs the
        // property to exist generically across MangaDex/AniList/MAL Settings). AniList's
        // RefreshToken() override is a no-op; this field is NEVER populated.
        [FieldDefinition(4, Label = "ImportListsAniListRefreshTokenLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string RefreshToken { get; set; }

        [FieldDefinition(5, Label = "ImportListsAniListExpiresLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public DateTime Expires { get; set; }

        // AuthUser is the AniList username extracted from the `Viewer { name }` follow-up query
        // after the pin→token exchange. Required for the `MediaListCollection(userName: ...)`
        // GraphQL query — AniList's list endpoint is keyed on username, not on the access token.
        [FieldDefinition(6, Label = "ImportListsAniListAuthUserLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string AuthUser { get; set; }

        // ── OAuth sign-in action surface (D-06 / D-07) ───────────────────────────────
        // FieldType.OAuth tells the FE to render a "Connect" button and POST to
        // /api/v5/importlist/action/{SignIn} on click. SignIn string itself is just the action
        // name ("startOAuth") — server-side RequestAction(action) dispatches. The FE then opens
        // the returned OauthUrl in a new tab and renders the AniListPinModal for paste-back.

        [FieldDefinition(7, Label = "ImportListsAniListSignInLabel", HelpText = "ImportListsAniListSignInHelpText", Type = FieldType.OAuth)]
        public string SignIn { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
