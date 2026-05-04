using Newtonsoft.Json;

namespace NzbDrone.Core.Notifications.Kavita
{
    // Kavita /api/Plugin/authenticate response — minimal shape per kavitareader.com API.
    // Only `token` is required for downstream Bearer auth; `username` is informational and
    // surfaced for diagnostics if Kavita ever returns an unexpected shape.
    public class KavitaAuthResponse
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }
    }
}
