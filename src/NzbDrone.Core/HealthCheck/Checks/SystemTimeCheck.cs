using NLog;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    public class SystemTimeCheck : HealthCheckBase
    {
        private readonly Logger _logger;

        // Sonarr divergence: Phase 15 D-21 — replaced services.sonarr.tv /time NTP-equivalent fetch with no-op
        // (no Mangarr cloud service; v1 does not validate system time against an upstream service).
        // v2 may reintroduce against a GitHub-hosted JSON endpoint or use a stub URL like worldtimeapi.org;
        // for now, return Ok unconditionally.
        public SystemTimeCheck(Logger logger, ILocalizationService localizationService)
            : base(localizationService)
        {
            _logger = logger;
        }

        public override HealthCheck Check()
        {
            // Sonarr divergence: Phase 15 D-21 — original implementation fetched
            // services.sonarr.tv/v1/time and compared against DateTime.UtcNow.
            // Mangarr has no cloud service; check is a no-op until v2 reintroduces a stub URL.
            _logger.Trace("SystemTimeCheck no-op (v1 ships without cloud time-comparison)");
            return new HealthCheck(GetType());
        }
    }

    public class ServiceTimeResponse
    {
        public System.DateTime DateTimeUtc { get; set; }
    }
}
