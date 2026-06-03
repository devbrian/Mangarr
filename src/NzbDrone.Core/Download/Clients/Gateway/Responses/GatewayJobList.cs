using System.Collections.Generic;

namespace NzbDrone.Core.Download.Clients.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>GET /downloads</c> 200 body:
    /// <c>{ jobs: [DownloadJob] }</c>. Maps to <c>IDownloadClient.GetItems</c>.
    /// </summary>
    public class GatewayJobList
    {
        public List<GatewayJob> Jobs { get; set; } = new List<GatewayJob>();
    }
}
