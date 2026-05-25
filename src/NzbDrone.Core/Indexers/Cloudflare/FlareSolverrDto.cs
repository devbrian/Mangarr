using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.Indexers.Cloudflare
{
    // Sonarr divergence: no Sonarr peer. Phase 33.2 — FlareSolverr/Byparr sidecar wire
    // contract POCOs. The two sidecars are API-compatible field-for-field on the
    // solution.cookies[] / solution.userAgent shape (RESEARCH §State of the Art —
    // Byparr is a drop-in FlareSolverr replacement). Newtonsoft attributes match the
    // documented lowercase JSON field names.
    //
    // REQUEST  → POST {solverUrl}/v1
    //   { "cmd": "request.get", "url": "https://comix.to/", "maxTimeout": 60000 }
    // RESPONSE ←
    //   { "status": "ok", "message": "...",
    //     "solution": { "userAgent": "Mozilla/5.0 ...",
    //                   "cookies": [ { "name": "cf_clearance", "value": "...",
    //                                  "domain": ".comix.to", "path": "/",
    //                                  "expires": 1610684149.3, "httpOnly": true,
    //                                  "secure": true } ] } }
    // Source: CITED github.com/FlareSolverr/FlareSolverr ;
    //         deepwiki.com/FlareSolverr/FlareSolverr/3-api-reference

    /// <summary>
    /// FlareSolverr/Byparr <c>POST /v1</c> request body. <c>Session</c> is intentionally
    /// omitted — the clearance service is stateless per RESEARCH §Alternatives Considered.
    /// </summary>
    public sealed class FlareSolverrRequest
    {
        [JsonProperty("cmd")]
        public string Cmd { get; set; } = "request.get";

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("maxTimeout")]
        public int MaxTimeout { get; set; } = 60000;
    }

    public sealed class FlareSolverrResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("solution")]
        public FlareSolverrSolution Solution { get; set; }
    }

    public sealed class FlareSolverrSolution
    {
        [JsonProperty("userAgent")]
        public string UserAgent { get; set; }

        [JsonProperty("cookies")]
        public List<FlareSolverrCookie> Cookies { get; set; }
    }

    public sealed class FlareSolverrCookie
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("domain")]
        public string Domain { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("expires")]
        public double? Expires { get; set; }

        [JsonProperty("httpOnly")]
        public bool? HttpOnly { get; set; }

        [JsonProperty("secure")]
        public bool? Secure { get; set; }
    }
}
