using Newtonsoft.Json;

namespace NzbDrone.Core.ImportLists.AniList.Resource
{
    // Phase 27 Plan 27-03 — AniList OAuth2 token-exchange response DTO.
    //
    // Endpoint: POST https://anilist.co/api/v2/oauth/token (form-urlencoded)
    // Verified shape (RESEARCH §STACK §Surface 2 + AniList docs.anilist.co/guide/auth/):
    //   {
    //     "token_type": "Bearer",
    //     "expires_in": 31536000,         // ~1 year (365 days)
    //     "access_token": "eyJ0eXAi..."
    //   }
    //
    // NOTE: AniList does NOT return a `refresh_token` field — the access token is a 1-year JWT
    // and users re-authenticate annually (D-07 / CONTEXT line 24). The Settings POCO carries
    // a RefreshToken property for IOAuthImportListSettings contract compliance but it's never
    // populated for AniList.
    //
    // Phase-wide JSON convention: Newtonsoft is configured project-wide with
    // CamelCasePropertyNamesContractResolver — but the OAuth2 spec mandates SNAKE_CASE field
    // names (access_token / expires_in / token_type), so we declare explicit [JsonProperty]
    // attributes that override the camelCase contract.
    public class AniListTokenResponse
    {
        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        // Seconds until access_token expiry — provider converts to DateTime via
        // DateTime.UtcNow.AddSeconds(ExpiresIn) and stores on Settings.Expires. AniList
        // currently issues ~31,536,000-second (1-year) lifetimes.
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
    }
}
