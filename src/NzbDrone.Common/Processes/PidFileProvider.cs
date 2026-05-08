using System;
using System.IO;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;

namespace NzbDrone.Common.Processes
{
    public interface IProvidePidFile
    {
        void Write();
    }

    public class PidFileProvider : IProvidePidFile
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;

        public PidFileProvider(IAppFolderInfo appFolderInfo, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _logger = logger;
        }

        public void Write()
        {
            if (OsInfo.IsWindows)
            {
                return;
            }

            // Sonarr divergence: Phase 15 D-08 — pid file sonarr.pid → mangarr.pid atomic with
            // app-data path Sonarr → Mangarr cutover. Matches AppFolderFactory.RemovePidFile().
            var filename = Path.Combine(_appFolderInfo.AppDataFolder, "mangarr.pid");
            try
            {
                File.WriteAllText(filename, ProcessProvider.GetCurrentProcessId().ToString());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to write PID file: " + filename);
                throw new SonarrStartupException(ex, "Unable to write PID file {0}", filename);
            }
        }
    }
}
