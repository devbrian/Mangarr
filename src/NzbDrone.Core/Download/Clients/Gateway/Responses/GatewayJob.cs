namespace NzbDrone.Core.Download.Clients.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>DownloadJob</c> schema
    /// (an element of <c>GET /downloads</c> and the body of <c>GET /downloads/{jobId}</c>).
    /// Required on the wire: <c>jobId</c>, <c>title</c>, <c>status</c>.
    ///
    /// <para>
    /// <see cref="Status"/> is the raw string enum
    /// <c>queued|resolving|downloading|archiving|completed|failed|warning|paused</c> — the client's
    /// <c>MapStatus</c> table (GWDL-03) maps it to <c>DownloadItemStatus</c>. <see cref="OutputPath"/>
    /// is host-reachable (after remote-path remap) only when the job is completed; it is nullable
    /// otherwise.
    /// </para>
    /// </summary>
    public class GatewayJob
    {
        // Required (OpenAPI required: [jobId, title, status]).
        public string JobId { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }

        public string SourceKey { get; set; }

        // Nullable until completed; remapped via IRemotePathMappingService before use (GWDL-03).
        public string OutputPath { get; set; }

        public int? TotalPages { get; set; }
        public int? DownloadedPages { get; set; }
        public long TotalBytes { get; set; }
        public long RemainingBytes { get; set; }

        public string Message { get; set; }
    }
}
