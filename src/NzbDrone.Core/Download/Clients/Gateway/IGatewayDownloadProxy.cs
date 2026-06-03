using NzbDrone.Core.Download.Clients.Gateway.Responses;

namespace NzbDrone.Core.Download.Clients.Gateway
{
    /// <summary>
    /// One method per gateway download endpoint — the HTTP-I/O half of the SABnzbd three-file
    /// decomposition (<c>origin/v5-develop:src/NzbDrone.Core/Download/Clients/Sabnzbd/SabnzbdProxy.cs</c>
    /// → <c>ISabnzbdProxy</c>). The client owns state-mapping + <c>Test()</c>; the proxy owns ALL
    /// transport (<c>X-Api-Key</c> header, deserialization, the exception ladder, and the two GWDL-04
    /// divergences).
    /// </summary>
    public interface IGatewayDownloadProxy
    {
        // POST /downloads — submit a grab handle, returns the gateway jobId (idempotent on releaseHandle).
        GatewaySubmitResponse Submit(GatewaySubmitRequest request, GatewayDownloadClientSettings settings);

        // GET /downloads — list live + recently-finished jobs.
        GatewayJobList GetJobs(GatewayDownloadClientSettings settings);

        // GET /downloads/{jobId} — single job (used to refine OutputPath).
        GatewayJob GetJob(string jobId, GatewayDownloadClientSettings settings);

        // DELETE /downloads/{jobId}?deleteData — remove a job (404 swallowed as idempotent success).
        void RemoveJob(string jobId, bool deleteData, GatewayDownloadClientSettings settings);

        // GET /status — output root folders + capabilities.
        GatewayStatusResponse GetStatus(GatewayDownloadClientSettings settings);

        // GET /version — version string for the Test() min-version gate.
        string GetVersion(GatewayDownloadClientSettings settings);
    }
}
