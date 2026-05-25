using System.Threading;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Cloudflare;
using NzbDrone.Core.Localization;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Sonarr divergence: no Sonarr peer. Phase 33.2 D-07 — a DISTINCT Cloudflare-solver
    // Health Check that surfaces "solver not configured" vs "solver unreachable" as two
    // textually-distinct messages, separate from the generic CF-403 / IndexerSourceUnavailable
    // message (which continues to flow through IndexerSourceFailureCheck unchanged, D-06).
    //
    // Analog: IndexerSourceFailureCheck (HealthCheckBase + [CheckOn] event triggers). UNLIKE the
    // analog this check keys off the app-wide CloudflareSolverUrl config + a reachability probe,
    // NOT the per-SourceKey disable list — so the IndexerSourceStatusChangedEvent trigger is
    // intentionally dropped (PATTERNS.md note). No new escalation machinery is added; persistent
    // CF still routes through IIndexerSourceStatusService.RecordFailure(...) untouched (D-06).

    /// <summary>
    /// Warns when the app-wide Cloudflare solver endpoint (<see cref="IConfigService.CloudflareSolverUrl"/>)
    /// is either NOT configured or is UNREACHABLE. The two states emit textually-distinct localized
    /// messages with distinct wiki anchors (D-07) so a user gets an unambiguous remedy. NEVER logs or
    /// surfaces the cf_clearance cookie value or the solver's HTML body (T-33.2-10 / ASVS V7).
    /// </summary>
    [CheckOn(typeof(ProviderUpdatedEvent<IIndexer>))]
    [CheckOn(typeof(ProviderDeletedEvent<IIndexer>))]
    [CheckOn(typeof(ProviderStatusChangedEvent<IIndexer>))]
    public class CloudflareSolverCheck : HealthCheckBase
    {
        private readonly IConfigService _configService;
        private readonly ICloudflareClearanceService _clearanceService;

        public CloudflareSolverCheck(
            IConfigService configService,
            ICloudflareClearanceService clearanceService,
            ILocalizationService localizationService)
            : base(localizationService)
        {
            _configService = configService;
            _clearanceService = clearanceService;
        }

        public override HealthCheck Check()
        {
            var solverUrl = _configService.CloudflareSolverUrl;

            if (solverUrl.IsNullOrWhiteSpace())
            {
                return new HealthCheck(
                    GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.IndexerStatusUnavailable,
                    _localizationService.GetLocalizedString("CloudflareSolverNotConfiguredHealthCheckMessage"),
                    "#cloudflare-solver-not-configured");
            }

            // Reachability probe: a probe-solve against a known aggregator host proves the sidecar
            // can actually clear, not merely answer. A connection/solve failure -> "unreachable".
            // We deliberately do NOT inspect the returned clearance (never touch the cookie value).
            try
            {
                _clearanceService.GetClearanceAsync("https://comix.to/", CancellationToken.None)
                                 .GetAwaiter().GetResult();
            }
            catch (CloudflareSolverNotConfiguredException)
            {
                // Race: the URL was cleared between the read above and the probe.
                return new HealthCheck(
                    GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.IndexerStatusUnavailable,
                    _localizationService.GetLocalizedString("CloudflareSolverNotConfiguredHealthCheckMessage"),
                    "#cloudflare-solver-not-configured");
            }
            catch
            {
                // Any solver/connection failure (CloudflareSolverException, HTTP timeout, etc.) ->
                // the sidecar is configured but not usable. Distinct "unreachable" message.
                return new HealthCheck(
                    GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.IndexerStatusUnavailable,
                    _localizationService.GetLocalizedString("CloudflareSolverUnreachableHealthCheckMessage"),
                    "#cloudflare-solver-unreachable");
            }

            return new HealthCheck(GetType());
        }

        // Probe-solve is a network call; do not run it on every startup tick. The config-empty
        // branch (the common misconfiguration) still runs on startup because it short-circuits
        // before the probe.
        public override bool CheckOnStartup => true;

        public override bool CheckOnSchedule => true;
    }
}
