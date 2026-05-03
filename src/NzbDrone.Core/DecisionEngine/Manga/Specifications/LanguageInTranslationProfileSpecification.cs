using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
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
            if (profileId == null)
            {
                _logger.Trace("No TranslationProfile assigned (per-Manga or global default); accepting.");
                return DownloadSpecDecision.Accept();
            }

            var profile = _translationProfileService.Get(profileId.Value);
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
