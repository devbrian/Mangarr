using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Role-match analog: TV MaximumSizeSpecification (size cap from global config).
    // IConfigService.MaximumSize is in MEGABYTES (int) — convert via NumberExtensions.Megabytes.
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class MaximumSizeSpecification : IMangaDecisionEngineSpecification
    {
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MaximumSizeSpecification(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            if (subject.Release == null)
            {
                return DownloadSpecDecision.Accept();
            }

            var maxMb = _configService.MaximumSize;
            if (maxMb == 0)
            {
                // 0 = no cap (mirrors TV semantics).
                return DownloadSpecDecision.Accept();
            }

            var maxBytes = maxMb.Megabytes();
            if (subject.Release.Size > maxBytes)
            {
                _logger.Debug("Release size {0} bytes exceeds cap {1} bytes ({2} MB)", subject.Release.Size, maxBytes, maxMb);
                return DownloadSpecDecision.Reject(
                    DownloadRejectionReason.SizeTooLarge,
                    "Release size {0} bytes exceeds configured cap of {1} MB",
                    subject.Release.Size,
                    maxMb);
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
