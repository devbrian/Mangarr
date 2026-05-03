using System;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Mirrors src/NzbDrone.Core/DecisionEngine/Specifications/MinimumAgeSpecification.cs (54 lines)
    // BUT skips the Usenet protocol check at TV-analog lines 24-28 since manga is HTTP-only
    // per Phase 4 D-10 — every manga release is downloadable immediately, but the operational
    // gate is preserved as a uniform-shape mirror so future protocols (e.g., Phase 6 manga torrents)
    // re-enable trivially.
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class MinimumAgeSpecification : IMangaDecisionEngineSpecification
    {
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MinimumAgeSpecification(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Temporary;   // mirrors TV — releases age into acceptance

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            // SKIP TV-analog Usenet protocol gate (Phase 4 D-10 — manga is HTTP-only).
            if (subject.Release == null)
            {
                return DownloadSpecDecision.Accept();
            }

            var minimumAge = _configService.MinimumAge;
            if (minimumAge == 0)
            {
                _logger.Debug("Minimum age is not set.");
                return DownloadSpecDecision.Accept();
            }

            var ageMinutes = subject.Release.AgeMinutes;
            var ageRounded = Math.Round(ageMinutes, 1);

            if (ageMinutes < minimumAge)
            {
                _logger.Debug("Only {0} minutes old, minimum age is {1} minutes", ageRounded, minimumAge);
                return DownloadSpecDecision.Reject(
                    DownloadRejectionReason.MinimumAge,
                    "Only {0} minutes old, minimum age is {1} minutes",
                    ageRounded,
                    minimumAge);
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
