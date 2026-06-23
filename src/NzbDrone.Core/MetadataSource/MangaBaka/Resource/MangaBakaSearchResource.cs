using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.MangaBaka.Resource
{
    /// <summary>
    /// Envelope for GET /v1/series/search — MangaBaka wraps every search response in
    /// <c>{ status, pagination, data[] }</c> (verified live: <c>data</c> is an ARRAY of
    /// series records). We deserialize the <c>data</c> array only — <c>MangaBakaApi.Search</c>
    /// returns it directly.
    ///
    /// Newtonsoft is configured project-wide with CamelCasePropertyNamesContractResolver
    /// (NzbDrone.Common/Serializer/Newtonsoft.Json/Json.cs), but MangaBaka ships snake_case
    /// keys (vs MangaDex's camelCase), so the DTOs carry explicit <c>[JsonProperty(...)]</c>
    /// attributes — the one shape difference from the MangaDex Resource trio.
    /// </summary>
    public class MangaBakaSearchResource
    {
        [JsonProperty("data")]
        public List<MangaBakaSeries> Data { get; set; }

        // Pagination is present on the envelope but not consumed by the provider; modelled
        // only so deserialization doesn't drop it silently if a caller later needs it.
        [JsonProperty("pagination")]
        public MangaBakaPagination Pagination { get; set; }
    }

    /// <summary>
    /// Search-envelope pagination block — optional, consumed by nothing today.
    /// </summary>
    public class MangaBakaPagination
    {
        [JsonProperty("page")]
        public int? Page { get; set; }

        [JsonProperty("limit")]
        public int? Limit { get; set; }

        // The MangaBaka envelope reports the total matching the filter as "count"
        // (e.g. 202528), NOT "total" — the Discovery toolbar's "N total match"
        // figure + the loop's computed-exhaustion guard read this.
        [JsonProperty("count")]
        public int? Total { get; set; }
    }
}
