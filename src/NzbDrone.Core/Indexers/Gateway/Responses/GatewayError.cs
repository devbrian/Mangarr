namespace NzbDrone.Core.Indexers.Gateway.Responses
{
    /// <summary>
    /// Hand-written Newtonsoft POCO mirroring the FROZEN OpenAPI <c>Error</c> schema. The outer
    /// envelope wraps a single nested <see cref="GatewayErrorBody"/> so <c>error.Error.Code</c>
    /// reads cleanly. <c>code</c> drives the kept FetchReleases exception ladder (A2):
    /// <c>auth</c> → ApiKeyException; <c>rate_limited</c> → TooManyRequestsException; else →
    /// IndexerException (never a swallowed null).
    /// </summary>
    public class GatewayError
    {
        public GatewayErrorBody Error { get; set; }
    }

    /// <summary>
    /// Inner error body (OpenAPI <c>Error.error</c>). <see cref="Code"/> enum on the wire:
    /// auth | rate_limited | source_unavailable | bad_request | internal.
    /// </summary>
    public class GatewayErrorBody
    {
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
