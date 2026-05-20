using Newtonsoft.Json;

namespace NzbDrone.Core.ImportLists.MyAnimeList.Resource
{
    // Phase 27 Plan 27-04 — MAL OAuth2 token-exchange response DTO.
    //
    // Endpoint: POST https://myanimelist.net/v1/oauth2/token (form-urlencoded)
    // Verified shape (RESEARCH §STACK §Surface 2 + https://myanimelist.net/apiconfig/references/authorization):
    //   {
    //     "token_type": "Bearer",
    //     "expires_in": 2592000,         // ~30 days (observed 31 days in field)
    //     "access_token": "eyJ0eXAi...",
    //     "refresh_token": "def50200..."
    //   }
    //
    // MAL ROTATES refresh tokens on each refresh-grant call — the provider's
    // RefreshToken() override applies the Trakt.cs:151 null-coalesce
    // (`Settings.RefreshToken = response.RefreshToken ?? Settings.RefreshToken`) to
    // gracefully handle either a rotated value or a server-side hold (MAL's policy
    // may change without notice).
    //
    // Phase-wide JSON convention: Newtonsoft is configured project-wide with
    // CamelCasePropertyNamesContractResolver — but the OAuth2 spec mandates SNAKE_CASE
    // field names (access_token / refresh_token / expires_in / token_type), so we
    // declare explicit [JsonProperty] attributes that override the camelCase contract.
    public class MalTokenResponse
    {
        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        // Seconds until access_token expiry — provider converts to DateTime via
        // DateTime.UtcNow.AddSeconds(ExpiresIn) and stores on Settings.Expires. MAL
        // currently issues ~2,592,000-second (30-day) lifetimes with rotation on every
        // refresh-grant call.
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }
    }
}
