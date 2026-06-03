using System.Collections.Generic;

namespace NzbDrone.Core.Indexers.Gateway
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>Capabilities</c> schema
    /// (NO codegen per STACK.md). Returned by <c>GET /caps</c> and cached 12h by
    /// <c>GatewayCapabilitiesProvider</c> (D-01). camelCase JSON maps to PascalCase C# via the
    /// default camelCase resolver.
    /// </summary>
    public class GatewayCapabilities
    {
        public string GatewayVersion { get; set; }

        public List<GatewaySourceCap> Sources { get; set; } = new List<GatewaySourceCap>();

        public List<string> SupportedSearchParams { get; set; } = new List<string>();

        public GatewayLimits Limits { get; set; } = new GatewayLimits();

        public List<string> DownloadFormats { get; set; } = new List<string>();
    }

    /// <summary>
    /// Mirrors the OpenAPI <c>SourceCap</c> schema — one aggregator source the gateway exposes.
    /// Required: key, name, enabled, supportsSearch, supportsRecent. The rest are advisory.
    /// </summary>
    public class GatewaySourceCap
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public bool SupportsSearch { get; set; }
        public bool SupportsRecent { get; set; }
        public List<string> IdTypes { get; set; }
        public List<string> Languages { get; set; }
        public int? RateLimitPerMinute { get; set; }
        public string Antibot { get; set; }
    }

    /// <summary>
    /// Mirrors the OpenAPI <c>Capabilities.limits</c> object — page-size bounds for search.
    /// </summary>
    public class GatewayLimits
    {
        public int DefaultPageSize { get; set; }
        public int MaxPageSize { get; set; }
    }
}
