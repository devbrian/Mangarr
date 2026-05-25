using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Indexers.Cloudflare
{
    // Sonarr divergence: no Sonarr peer. Phase 33.2 — typed clearance failures.

    /// <summary>
    /// Raised when the configured FlareSolverr/Byparr sidecar fails to produce a usable
    /// clearance (non-"ok" status, or no <c>cf_clearance</c> cookie in the solution).
    /// Carries host + solver message only — NEVER the cf_clearance value or the solver's
    /// full HTML response (T-33.2-01 / ASVS V7).
    /// </summary>
    public class CloudflareSolverException : NzbDroneException
    {
        public CloudflareSolverException(string message)
            : base(message)
        {
        }

        public CloudflareSolverException(string message, params object[] args)
            : base(message, args)
        {
        }
    }

    /// <summary>
    /// Distinct subtype raised when no solver URL is configured at all. Kept separate from
    /// <see cref="CloudflareSolverException"/> so the Plan 03 D-07 Health Check can emit a
    /// distinct "not configured" message (vs the generic "unreachable / solve failed").
    /// </summary>
    public class CloudflareSolverNotConfiguredException : CloudflareSolverException
    {
        public CloudflareSolverNotConfiguredException()
            : base("Cloudflare solver URL is not configured")
        {
        }
    }
}
