using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.MangaBaka.Resource
{
    /// <summary>
    /// Envelope for GET /v1/series/{id} — single-record response wrapping one
    /// <see cref="MangaBakaSeries"/> in <c>data</c>. Per MangaBaka's <c>{ status, data }</c>
    /// shape.
    ///
    /// Open Q3: <c>data</c> is assumed to be an OBJECT (single record) for the by-id
    /// endpoint — the executor verifies with one live <c>GET /v1/series/3397</c> when
    /// wiring the provider (Plan 41-03); only this DTO's <c>Data</c> cardinality changes
    /// if that assumption is wrong (cheap fast-fail).
    /// </summary>
    public class MangaBakaSeriesResource
    {
        [JsonProperty("data")]
        public MangaBakaSeries Data { get; set; }
    }
}
