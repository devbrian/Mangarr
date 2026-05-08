using System.Collections.Generic;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV CheckOn(EpisodeImported|FailedEvent) attributes
    // stripped (events DELETED). Manga peer events (ChapterImportedEvent / ChapterImportFailedEvent) may rewire
    // in v1.x; for now the check fires only on its CheckOnSchedule cadence.
    public class RecyclingBinCheck : HealthCheckBase
    {
        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;

        public RecyclingBinCheck(IConfigService configService, IDiskProvider diskProvider, ILocalizationService localizationService)
            : base(localizationService)
        {
            _configService = configService;
            _diskProvider = diskProvider;
        }

        public override HealthCheck Check()
        {
            var recycleBin = _configService.RecycleBin;

            if (recycleBin.IsNullOrWhiteSpace())
            {
                return new HealthCheck(GetType());
            }

            if (!_diskProvider.FolderWritable(recycleBin))
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Error,
                    HealthCheckReason.RecycleBinUnableToWrite,
                    _localizationService.GetLocalizedString("RecycleBinUnableToWriteHealthCheckMessage", new Dictionary<string, object>
                    {
                        { "path", recycleBin }
                    }),
                    "#cannot-write-recycle-bin");
            }

            return new HealthCheck(GetType());
        }
    }
}
