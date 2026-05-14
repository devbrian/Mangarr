using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW spec class with no TV analog (Sonarr has no TranslationProfile equivalent).
    // TPROFILE outer gate per Phase 5 D-06 — applied BEFORE CF inner score path
    // (cf-only-walkthrough.md verdict signed off 2026-05-01). See DIVERGENCE.md.
    // Composed from CustomFormatAllowedByProfileSpecification profile-injection shape
    // (src/NzbDrone.Core/DecisionEngine/Specifications/CustomFormatAllowedByProfileSpecification.cs:1-35)
    // + MonitoredEpisodeSpecification interface-impl shape
    // (src/NzbDrone.Core/DecisionEngine/Specifications/RssSync/MonitoredEpisodeSpecification.cs:1-57)
    // per PATTERNS.md line 1611.
    // Phase 8 cleanup: collapse with TV CustomFormatAllowedByProfileSpecification when Tv/ deletes.
    public class LanguageInTranslationProfileSpecification : IMangaDecisionEngineSpecification
    {
        private readonly ITranslationProfileService _translationProfileService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public LanguageInTranslationProfileSpecification(
            ITranslationProfileService translationProfileService,
            IConfigService configService,
            Logger logger)
        {
            _translationProfileService = translationProfileService;
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            var profileId = subject.Manga?.TranslationProfileId ?? _configService.DefaultTranslationProfileId;

            // BL-03 (DEF-19-02-01): a Manga can carry TranslationProfileId == 0 — the int
            // default. 0 is never a real FK (ids start at 1); treat it the same as "no
            // profile assigned" — Accept.
            if (profileId == null || profileId.Value <= 0)
            {
                _logger.Trace("No usable TranslationProfile (per-Manga or global default); accepting.");
                return DownloadSpecDecision.Accept();
            }

            // WR-02: orphaned FK guard. ITranslationProfileService.Get delegates to
            // BasicRepository.Get which THROWS ModelNotFoundException on a missing row — the
            // original `profile == null` guard never actually fired because Get throws
            // rather than returning null. Left unguarded that throw is caught by
            // MangaDownloadDecisionMaker.EvaluateSpec and turns EVERY release into a
            // DecisionError rejection, so the InteractiveSearch modal renders a results
            // table the React row code then crashes on. Catch the not-found exception and
            // treat a stale / deleted profile id as "no profile assigned" — Accept.
            TranslationProfile profile;
            try
            {
                profile = _translationProfileService.Get(profileId.Value);
            }
            catch (ModelNotFoundException)
            {
                _logger.Warn("TranslationProfile {0} not found (orphaned FK); accepting", profileId.Value);
                return DownloadSpecDecision.Accept();
            }

            if (profile == null)
            {
                _logger.Warn("TranslationProfile {0} not found (orphaned FK); accepting", profileId.Value);
                return DownloadSpecDecision.Accept();
            }

            var releaseLanguage = subject.Release?.TranslatedLanguage;

            if (string.IsNullOrEmpty(releaseLanguage))
            {
                if (profile.AllowLanguagesNotInProfile)
                {
                    return DownloadSpecDecision.Accept();
                }

                return DownloadSpecDecision.Reject(
                    DownloadRejectionReason.LanguageNotInProfile,
                    "Release has no TranslatedLanguage and profile '{0}' rejects unknown languages",
                    profile.Name);
            }

            var isInProfile = profile.Languages != null
                && profile.Languages.Any(l => string.Equals(l, releaseLanguage, StringComparison.OrdinalIgnoreCase));

            if (!isInProfile && !profile.AllowLanguagesNotInProfile)
            {
                return DownloadSpecDecision.Reject(
                    DownloadRejectionReason.LanguageNotInProfile,
                    "Release language '{0}' not in profile '{1}' (strict mode per Phase 5 D-02)",
                    releaseLanguage,
                    profile.Name);
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
