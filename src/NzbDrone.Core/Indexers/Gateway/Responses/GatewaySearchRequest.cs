using System.Collections.Generic;

namespace NzbDrone.Core.Indexers.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>SearchRequest</c> schema —
    /// the JSON body POSTed to <c>/search</c> (built by the request generator in Plan 02).
    /// Required: type (manga|chapter). camelCase JSON via the default resolver.
    /// </summary>
    public class GatewaySearchRequest
    {
        public string Type { get; set; }
        public string Query { get; set; }
        public Dictionary<string, object> Ids { get; set; }
        public decimal? Chapter { get; set; }
        public int? Volume { get; set; }
        public List<string> Languages { get; set; }
        public List<string> Sources { get; set; }
        public bool Interactive { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }
}
