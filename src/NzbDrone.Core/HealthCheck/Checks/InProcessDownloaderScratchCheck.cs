using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    /// <summary>
    /// Phase 4 Health Check — surfaces in-process downloader scratch-dir misconfiguration.
    /// Fires <see cref="HealthCheckResult.Warning"/> if <see cref="IConfigService.DownloadScratchPath"/>
    /// is missing/uncreatable OR free space &lt; 1 GB.
    /// Mirrors the existing <see cref="DownloadClientRootFolderCheck"/> + disk-space pattern.
    /// </summary>
    public class InProcessDownloaderScratchCheck : HealthCheckBase
    {
        private const long OneGigabyte = 1L * 1024 * 1024 * 1024;

        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public InProcessDownloaderScratchCheck(IConfigService configService,
                                               IDiskProvider diskProvider,
                                               ILocalizationService localizationService,
                                               Logger logger)
            : base(localizationService)
        {
            _configService = configService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public override HealthCheck Check()
        {
            var path = _configService.DownloadScratchPath;

            if (!_diskProvider.FolderExists(path))
            {
                try
                {
                    _diskProvider.CreateFolder(path);
                }
                catch
                {
                    _logger.Warn("In-process downloader scratch dir '{0}' does not exist and could not be created", path);
                    return new HealthCheck(GetType(),
                        HealthCheckResult.Warning,
                        HealthCheckReason.InProcessDownloaderScratchPathMissing,
                        $"Phase 4 in-process downloader scratch dir '{path}' does not exist and could not be created.",
                        "#in-process-downloader-scratch-path");
                }
            }

            var freeSpace = _diskProvider.GetAvailableSpace(path) ?? 0L;
            if (freeSpace < OneGigabyte)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.InProcessDownloaderScratchLowSpace,
                    $"In-process downloader scratch path '{path}' has less than 1 GB free.",
                    "#in-process-downloader-low-space");
            }

            return new HealthCheck(GetType());
        }
    }
}
