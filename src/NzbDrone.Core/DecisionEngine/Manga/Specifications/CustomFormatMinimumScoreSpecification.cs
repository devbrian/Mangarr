using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Profiles.CustomFormats;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga-side CF inner gate per Phase 5 D-07 — see DIVERGENCE.md.
    // Direct mirror of CustomFormatAllowedByProfileSpecification at
    // src/NzbDrone.Core/DecisionEngine/Specifications/CustomFormatAllowedByProfileSpecification.cs (35 lines)
    // with profile-source swap to ICustomFormatProfileService.
    // ADAPTATION HOTSPOT 4: honors NULLABLE MaxFormatScore (null = no upper cap; Sonarr's
    // QualityProfile has no max-score concept).
    // Phase 8 cleanup: collapse with TV CustomFormatAllowedByProfileSpecification when Tv/ deletes.
    public class CustomFormatMinimumScoreSpecification : IMangaDecisionEngineSpecification
    {
        private readonly ICustomFormatProfileService _profileService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public CustomFormatMinimumScoreSpecification(
            ICustomFormatProfileService profileService,
            IConfigService configService,
            Logger logger)
        {
            _profileService = profileService;
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            var profileId = subject.Manga?.CustomFormatProfileId ?? _configService.DefaultCustomFormatProfileId;
            if (profileId == null)
            {
                return DownloadSpecDecision.Accept();
            }

            var profile = _profileService.Get(profileId.Value);

            // WR-02: orphaned FK guard. The service's in-use protection blocks normal deletes,
            // but cannot protect against direct DB edits, restore-from-backup that drops the
            // referenced profile, or a stale Manga.CustomFormatProfileId after a deletion race.
            // Treat null profile as "no profile assigned" (mirrors the profileId == null branch
            // above) — falls through to Accept rather than NRE'ing on profile.MinFormatScore.
            if (profile == null)
            {
                _logger.Warn("CustomFormatProfile {0} not found (orphaned FK); accepting", profileId.Value);
                return DownloadSpecDecision.Accept();
            }

            var score = subject.CustomFormatScore;
            var formats = subject.CustomFormats?.ConcatToString() ?? "(none)";

            if (score < profile.MinFormatScore)
            {
                return DownloadSpecDecision.Reject(
                    DownloadRejectionReason.CustomFormatMinimumScore,
                    "Custom Formats {0} have score {1} below profile '{2}' minimum {3}",
                    formats,
                    score,
                    profile.Name,
                    profile.MinFormatScore);
            }

            if (profile.MaxFormatScore.HasValue && score > profile.MaxFormatScore.Value)
            {
                return DownloadSpecDecision.Reject(
                    DownloadRejectionReason.CustomFormatMaximumScore,
                    "Custom Formats score {0} exceeds profile '{1}' maximum {2}",
                    score,
                    profile.Name,
                    profile.MaxFormatScore.Value);
            }

            _logger.Trace(
                "Custom Format Score of {0} [{1}] within profile '{2}' bounds [{3}, {4}]",
                score,
                formats,
                profile.Name,
                profile.MinFormatScore,
                profile.MaxFormatScore?.ToString() ?? "(no cap)");
            return DownloadSpecDecision.Accept();
        }
    }
}
