using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
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
            // WR-08: prefer the maker-stamped resolved id so score and gate are keyed off
            // the SAME profile. Fall back to the per-Manga FK ?? global default chain only
            // when the spec is invoked outside the maker (Phase 6 / future call sites that
            // don't hydrate ResolvedCustomFormatProfileId).
            var profileId = subject.ResolvedCustomFormatProfileId
                            ?? subject.Manga?.CustomFormatProfileId
                            ?? _configService.DefaultCustomFormatProfileId;

            // BL-03 (DEF-19-02-01): a Manga can carry CustomFormatProfileId == 0 — the int
            // default the AddManga modal sends when no CF profile is chosen and none is the
            // global default. 0 is never a real FK (ids start at 1); treat it the same as
            // "no profile assigned" — Accept.
            if (profileId == null || profileId.Value <= 0)
            {
                return DownloadSpecDecision.Accept();
            }

            // WR-02: orphaned FK guard. ICustomFormatProfileService.Get delegates to
            // BasicRepository.Get which THROWS ModelNotFoundException on a missing row — the
            // original `profile == null` guard never actually fired because Get throws
            // rather than returning null. Left unguarded that throw is caught by
            // MangaDownloadDecisionMaker.EvaluateSpec and turns EVERY release into a
            // DecisionError rejection, so the InteractiveSearch modal renders a results
            // table the React row code then crashes on. Catch the not-found exception and
            // treat a stale / deleted profile id as "no profile assigned" — Accept.
            CustomFormatProfile profile;
            try
            {
                profile = _profileService.Get(profileId.Value);
            }
            catch (ModelNotFoundException)
            {
                _logger.Warn("CustomFormatProfile {0} not found (orphaned FK); accepting", profileId.Value);
                return DownloadSpecDecision.Accept();
            }

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
