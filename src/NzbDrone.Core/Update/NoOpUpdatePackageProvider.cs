using System;
using System.Collections.Generic;
using NLog;

namespace NzbDrone.Core.Update
{
    public interface IUpdatePackageProvider
    {
        UpdatePackage GetLatestUpdate(string branch, Version currentVersion);
        List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null);
    }

    // Sonarr divergence: Phase 15 D-21 — replaces UpdatePackageProvider (which fetched manifests from
    // services.sonarr.tv via ISonarrCloudRequestBuilder, both deleted). v1 ships with no auto-update;
    // ManualUpdateCheck health check surfaces "manually update via GitHub Releases" guidance to the user.
    // v2 may swap this for a GitHubReleasesUpdatePackageProvider (DIST-03 alignment).
    public class NoOpUpdatePackageProvider : IUpdatePackageProvider
    {
        private readonly Logger _logger;

        public NoOpUpdatePackageProvider(Logger logger)
        {
            _logger = logger;
        }

        public UpdatePackage GetLatestUpdate(string branch, Version currentVersion)
        {
            _logger.Debug("NoOpUpdatePackageProvider.GetLatestUpdate called (v1 no-op; manual updates via GitHub Releases)");
            return null;
        }

        public List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null)
        {
            _logger.Debug("NoOpUpdatePackageProvider.GetRecentUpdates called (v1 no-op)");
            return new List<UpdatePackage>();
        }
    }
}
