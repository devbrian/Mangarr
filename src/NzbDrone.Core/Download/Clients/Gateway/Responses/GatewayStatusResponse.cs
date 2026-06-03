using System.Collections.Generic;

namespace NzbDrone.Core.Download.Clients.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>StatusResponse</c> schema
    /// (<c>GET /status</c>). Required on the wire: <c>isLocalhost</c>, <c>outputRootFolders</c>.
    /// Maps to <c>IDownloadClient.GetStatus</c> (GWDL-03 — every <see cref="OutputRootFolders"/>
    /// entry is remapped via <c>IRemotePathMappingService</c>).
    /// </summary>
    public class GatewayStatusResponse
    {
        // Required (OpenAPI required: [isLocalhost, outputRootFolders]).
        public bool IsLocalhost { get; set; }
        public List<string> OutputRootFolders { get; set; } = new List<string>();

        public bool RemovesCompletedDownloads { get; set; }
        public string Version { get; set; }

        public GatewayStatusCapabilities Capabilities { get; set; }
    }

    /// <summary>
    /// Inner capability flags (OpenAPI <c>StatusResponse.capabilities</c>).
    /// </summary>
    public class GatewayStatusCapabilities
    {
        public bool Pause { get; set; }
        public List<string> OutputFormats { get; set; }
        public int MaxConcurrentChapters { get; set; }
    }
}
