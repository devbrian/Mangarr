using System;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.MangaDex
{
    // Phase 27 Plan 27-02 (D-01, D-08) — MangaDex follows-list provider Settings POCO.
    //
    // Pattern source (verbatim shape):
    //   .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktSettings.cs:18-46
    //
    // Phase 27 augments vs Trakt verbatim:
    //   * Add `Privacy = PrivacyLevel.Password` alongside `Hidden = HiddenType.Hidden` on
    //     every token-bearing field (AccessToken/RefreshToken/Expires/AuthUser) — Mangarr
    //     ImportList Settings carry this for defense-in-depth on the V5 controller's
    //     outbound JSON redaction path (T-27-02-V4 mitigation).
    //   * Inherit `ImportListSettingsBase<MangaDexImportListSettings>` (Phase 26 substrate)
    //     NOT `NotificationSettingsBase<TSettings>`.
    //   * Implement `IOAuthImportListSettings` so `OAuthAwareImportListBase<TSettings>` can
    //     drive `RefreshTokenIfNecessary()` generically (Plan 27-01 base contract).
    //   * Surface user-visible credential fields BEFORE the hidden token block:
    //     `ClientId` (0) / `ClientSecret` (1, Password) / `Username` (2) / `Password` (3, Password).
    //     MangaDex personal-client OAuth requires user-supplied client_id + client_secret
    //     per https://api.mangadex.org/docs/02-authentication/personal-clients/; no Mangarr-pinned
    //     centralized credentials (unlike Sonarr-Trakt's pinned ClientId per TraktProxy.cs:26).
    //
    // D-01 enforcement: tokens stored as plain string / DateTime — zero crypto-at-rest
    // infrastructure of any shape (no key-protection provider, no platform secret-store
    // bridge, no per-row sealed blob). ASVS V8 EXPLICITLY EXCLUDED per Phase 27 charter;
    // matches Sonarr verbatim. See IOAuthImportListSettings.cs for the contract-level
    // audit signal.
    //
    // D-08 enforcement: `SignIn = "startOAuth"` mirrors TraktSettings.cs:22-25 ctor pattern;
    // V5 FE's FieldType.OAuth renderer wires the click handler to POST
    // /api/v5/importlist/action/MangaDex with body `{ name: "startOAuth", query: {} }` —
    // server-side RequestAction("startOAuth") runs the password-grant verify-credentials
    // probe and persists the resulting AccessToken/RefreshToken on this POCO. NO external
    // OAuth redirect (D-08 internal-only flow).
    public class MangaDexImportListSettingsValidator : AbstractValidator<MangaDexImportListSettings>
    {
        public MangaDexImportListSettingsValidator()
        {
            // Initial-save guardrails: user-supplied credentials MUST be present before
            // the FE "Test & Connect" affordance triggers RequestAction("startOAuth").
            RuleFor(c => c.ClientId).NotEmpty();
            RuleFor(c => c.ClientSecret).NotEmpty();
            RuleFor(c => c.Username).NotEmpty();
            RuleFor(c => c.Password).NotEmpty();

            // Post-exchange guardrails: after a successful PasswordGrant the token block
            // must round-trip into Settings JSON. The FE Save handler invokes Validate()
            // on every PUT — these rules ensure the user cannot persist a half-exchanged
            // state where the credentials exist but the tokens were silently dropped.
            // Mirrors Trakt's TraktSettingsValidator (TraktSettings.cs:10-15) verbatim shape.
            RuleFor(c => c.AccessToken)
                .NotEmpty()
                .When(c => c.Expires != default(DateTime));

            RuleFor(c => c.RefreshToken)
                .NotEmpty()
                .When(c => c.Expires != default(DateTime));
        }
    }

    public class MangaDexImportListSettings : ImportListSettingsBase<MangaDexImportListSettings>, IOAuthImportListSettings
    {
        private static readonly MangaDexImportListSettingsValidator Validator = new();

        public MangaDexImportListSettings()
        {
            // BaseUrl is fixed for the follows endpoint root — MangaDex doesn't ship
            // self-hosted instances, so we hard-code the canonical host and surface no
            // [FieldDefinition] (T-CONFIG-DRIFT-01 mitigation by absence, mirrors
            // MangaDexIndexerSettings.cs:47).
            BaseUrl = "https://api.mangadex.org";

            // D-06 / D-08 Sonarr-canonical OAuth action surface — mirrors
            // TraktSettings.cs:24 verbatim. The FE FieldType.OAuth control reads this
            // string and routes the click to POST /api/v5/importlist/action/{SignIn}.
            SignIn = "startOAuth";
        }

        // IImportListSettings contract requirement (declared abstract on
        // ImportListSettingsBase<TSettings>). MangaDex doesn't expose this in the UI
        // — fixed at the api.mangadex.org host per ToS.
        public override string BaseUrl { get; set; }

        // ── User-visible credential block (D-08) ─────────────────────────────────────
        // MangaDex personal-client OAuth requires user-supplied client_id + client_secret
        // because MangaDex does not host centralized Mangarr-pinned credentials. Users
        // register a personal client at https://mangadex.org/settings/api-clients.

        [FieldDefinition(0, Label = "ImportListsMangaDexClientIdLabel", HelpText = "ImportListsMangaDexClientIdHelpText", Type = FieldType.Textbox)]
        public string ClientId { get; set; }

        [FieldDefinition(1, Label = "ImportListsMangaDexClientSecretLabel", HelpText = "ImportListsMangaDexClientSecretHelpText", Type = FieldType.Textbox, Privacy = PrivacyLevel.Password)]
        public string ClientSecret { get; set; }

        [FieldDefinition(2, Label = "ImportListsMangaDexUsernameLabel", HelpText = "ImportListsMangaDexUsernameHelpText", Type = FieldType.Textbox)]
        public string Username { get; set; }

        [FieldDefinition(3, Label = "ImportListsMangaDexPasswordLabel", HelpText = "ImportListsMangaDexPasswordHelpText", Type = FieldType.Textbox, Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        // ── Hidden OAuth token block (D-01 + T-27-02-V4 mitigation) ──────────────────
        // Hidden = HiddenType.Hidden suppresses UI rendering; Privacy = PrivacyLevel.Password
        // marks the field for V5 controller outbound JSON redaction (ProviderControllerBase
        // strips Hidden fields from the schema response).

        [FieldDefinition(4, Label = "ImportListsMangaDexAccessTokenLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden, Privacy = PrivacyLevel.Password)]
        public string AccessToken { get; set; }

        [FieldDefinition(5, Label = "ImportListsMangaDexRefreshTokenLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden, Privacy = PrivacyLevel.Password)]
        public string RefreshToken { get; set; }

        [FieldDefinition(6, Label = "ImportListsMangaDexExpiresLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden, Privacy = PrivacyLevel.Password)]
        public DateTime Expires { get; set; }

        [FieldDefinition(7, Label = "ImportListsMangaDexAuthUserLabel", Type = FieldType.Textbox, Hidden = HiddenType.Hidden, Privacy = PrivacyLevel.Password)]
        public string AuthUser { get; set; }

        // ── OAuth sign-in action surface (D-06 / D-08) ───────────────────────────────
        // FieldType.OAuth tells the FE to render a "Test & Connect" button and POST to
        // /api/v5/importlist/action/{SignIn} on click. SignIn string itself is just the
        // action name ("startOAuth") — server-side RequestAction(action) dispatches.

        [FieldDefinition(8, Label = "ImportListsMangaDexSignInLabel", HelpText = "ImportListsMangaDexSignInHelpText", Type = FieldType.OAuth)]
        public string SignIn { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
