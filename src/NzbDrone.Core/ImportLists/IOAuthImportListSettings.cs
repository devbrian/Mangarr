using System;

namespace NzbDrone.Core.ImportLists
{
    // Phase 27 Plan 27-01 (D-01) — Sonarr-canonical PLAINTEXT OAuth-bearing Settings
    // contract. Mirrors the property shape exposed by
    // .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktSettings.cs:27-37
    // verbatim. The 3 Phase 27 provider Settings POCOs (MangaDex follows / AniList list /
    // MAL list) implement this interface so the shared OAuthAwareImportListBase can drive
    // the Trakt-canonical RefreshTokenIfNecessary() template generically.
    //
    // D-01 enforcement: properties are plain string/DateTime — zero crypto-at-rest
    // infrastructure of any shape (no key-protection provider, no platform secret-store
    // bridge, no per-row sealed blob). Sonarr-canonical plaintext (matches Sonarr verbatim
    // per user directive 2026-05-20). Per-provider
    // Settings POCOs annotate the AccessToken/RefreshToken/Expires/AuthUser fields with
    // [FieldDefinition(Hidden = HiddenType.Hidden)] to suppress UI rendering and rely on
    // the V5 controller's outbound schema redaction (ProviderControllerBase strips Hidden
    // fields from schema GET responses). Sonarr-canonical TraktSettings.cs:27-37 pattern —
    // do NOT add `Privacy = PrivacyLevel.Password` on Hidden token fields; the combo
    // breaks the new-Add round-trip (FormatException on `DateTime.Parse("********")`).
    // This contract does NOT enforce the annotations (Settings POCO authors own that
    // discipline).
    public interface IOAuthImportListSettings : IImportListSettings
    {
        string AccessToken { get; set; }
        string RefreshToken { get; set; }
        DateTime Expires { get; set; }
        string AuthUser { get; set; }
    }
}
