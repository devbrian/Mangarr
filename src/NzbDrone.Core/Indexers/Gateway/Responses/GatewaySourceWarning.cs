namespace NzbDrone.Core.Indexers.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>SourceWarning</c> schema.
    /// Per-source soft failures are reported in <c>ReleaseListResponse.warnings[]</c> (NOT as an
    /// HTTP error). The parser (Plan 03) maps each warning to
    /// <c>IIndexerSourceStatusService.RecordFailure(sourceKey)</c> — D-03a — without throwing.
    /// </summary>
    public class GatewaySourceWarning
    {
        public string SourceKey { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
