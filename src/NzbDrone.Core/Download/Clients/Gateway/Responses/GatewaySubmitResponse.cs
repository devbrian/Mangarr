namespace NzbDrone.Core.Download.Clients.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>SubmitResponse</c> schema.
    /// Returned for BOTH HTTP 200 (accepted) AND HTTP 400 (rejected) — the deliberate GWDL-04
    /// divergence (Pitfall 1): a 400 carries a <c>SubmitResponse</c> body, NOT the standard
    /// <c>Error</c> envelope.
    ///
    /// <para>
    /// <see cref="JobId"/> is a NULLABLE reference type: <c>null</c> signals rejection (the client
    /// translates that to <c>DownloadClientRejectedReleaseException</c>); a non-null value IS the
    /// <c>DownloadId</c> (idempotent on <c>releaseHandle</c>, GWDL-02). It MUST stay a
    /// <c>string</c> (reference type) so a 400 body deserializes with <c>JobId == null</c> rather
    /// than the default-zero an <c>int</c> would yield.
    /// </para>
    /// </summary>
    public class GatewaySubmitResponse
    {
        // Nullable: null on rejection (GWDL-04); else the DownloadId (GWDL-02).
        public string JobId { get; set; }

        // queued | resolving on the wire.
        public string Status { get; set; }

        public string Message { get; set; }
    }
}
