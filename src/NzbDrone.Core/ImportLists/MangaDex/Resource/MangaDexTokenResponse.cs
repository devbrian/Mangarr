using Newtonsoft.Json;

namespace NzbDrone.Core.ImportLists.MangaDex.Resource
{
    // Phase 27 Plan 27-02 — Keycloak token response DTO for MangaDex personal-client
    // OAuth2 password / refresh grants.
    //
    // Endpoint: POST https://auth.mangadex.org/realms/mangadex/protocol/openid-connect/token
    // Verified shape (RESEARCH §STACK §Surface 2 + Keycloak OIDC spec):
    //   {
    //     "access_token": "eyJ...",
    //     "refresh_token": "eyJ...",
    //     "expires_in": 900,          // 15-minute access lifetime
    //     "refresh_expires_in": 2592000,  // ~30-day refresh lifetime
    //     "token_type": "Bearer",
    //     "scope": "openid profile"
    //   }
    //
    // Phase-wide JSON convention: Newtonsoft is configured project-wide with
    // CamelCasePropertyNamesContractResolver — but Keycloak emits SNAKE_CASE field
    // names (access_token / refresh_token / expires_in), so we declare explicit
    // [JsonProperty] attributes that override the camelCase contract.
    public class MangaDexTokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        // Seconds until access_token expiry — provider converts to DateTime via
        // DateTime.UtcNow.AddSeconds(ExpiresIn) and stores on Settings.Expires.
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("refresh_expires_in")]
        public int? RefreshExpiresIn { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }
    }
}
