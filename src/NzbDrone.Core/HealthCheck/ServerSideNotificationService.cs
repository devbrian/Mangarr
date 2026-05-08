using NLog;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck
{
    // Sonarr divergence: Phase 15 D-21 — original implementation fetched server-side notifications from
    // services.sonarr.tv/v1/notification (deprecation notices, package warnings, etc.). Mangarr has no
    // cloud service. v1 ships with this stubbed to a no-op; v2 may reintroduce against a GitHub-hosted
    // JSON manifest (DIST-03 alignment). Per archived 08-08 Sub-step E disposition.
    public class ServerSideNotificationService : HealthCheckBase
    {
        private readonly Logger _logger;

        public ServerSideNotificationService(ILocalizationService localizationService, Logger logger)
            : base(localizationService)
        {
            _logger = logger;
        }

        public override HealthCheck Check()
        {
            _logger.Trace("ServerSideNotificationService no-op (Mangarr has no cloud service)");
            return new HealthCheck(GetType());
        }
    }

    public class ServerNotificationResponse
    {
        public HealthCheckResult Type { get; set; }
        public string Message { get; set; }
        public string WikiUrl { get; set; }
    }
}
