using Newtonsoft.Json;

namespace NzbDrone.Core.Notifications.Komga
{
    // Komga API DTO — minimal subset; full schema at https://komga.org/docs/openapi/.
    // Used by Test() connectivity probe + Phase 7 Settings UI dropdown seed.
    public class KomgaLibrary
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("root")]
        public string Root { get; set; }
    }
}
