using System.Collections.Generic;

namespace NzbDrone.Core.Indexers.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>ReleaseListResponse</c>
    /// schema — the 200 body of both <c>POST /search</c> and <c>GET /recent</c>. Required:
    /// releases. Per-source soft failures arrive in <see cref="Warnings"/> (D-03a), never as an
    /// HTTP error.
    /// </summary>
    public class GatewaySearchResponse
    {
        public List<GatewayRelease> Releases { get; set; } = new List<GatewayRelease>();

        public List<GatewaySourceWarning> Warnings { get; set; } = new List<GatewaySourceWarning>();
    }
}
