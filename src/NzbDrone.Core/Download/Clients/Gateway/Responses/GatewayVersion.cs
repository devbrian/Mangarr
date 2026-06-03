namespace NzbDrone.Core.Download.Clients.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>GET /version</c> 200 body:
    /// <c>{ version, status }</c> (<c>status</c> = <c>ok|degraded</c>). The shared health probe
    /// for both surfaces; the client's <c>Test()</c> uses <see cref="Version"/> for the min-version
    /// gate (GWDL-01).
    /// </summary>
    public class GatewayVersion
    {
        public string Version { get; set; }

        // ok | degraded on the wire.
        public string Status { get; set; }
    }
}
