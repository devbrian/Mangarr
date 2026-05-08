using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Sonarr divergence: Phase 15 D-21 — surfaces "manual update" guidance in System -> Health.
    // v1 ships without auto-update (Cloud/SonarrCloudRequestBuilder deleted; UpdatePackageProvider replaced
    // by NoOpUpdatePackageProvider). v2 may add GitHubReleasesUpdatePackageProvider then this check
    // updates to "auto-update available" semantics (DIST-03).
    [CheckOn(typeof(ApplicationStartedEvent))]
    public class ManualUpdateCheck : HealthCheckBase
    {
        public ManualUpdateCheck(ILocalizationService localizationService)
            : base(localizationService)
        {
        }

        public override HealthCheck Check()
        {
            return new HealthCheck(
                GetType(),
                HealthCheckResult.Notice,
                HealthCheckReason.UpdateAvailable,
                "Mangarr v1 ships with manual updates only. Check GitHub Releases for new versions.",
                "#manga-manual-updates"
            );
        }

        public override bool CheckOnSchedule => false;
    }
}
