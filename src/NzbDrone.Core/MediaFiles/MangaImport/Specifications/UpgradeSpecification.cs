using System;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.MediaFiles.MangaImport.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-10 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/
    // UpgradeSpecification.cs.
    //
    // D-10 three-state effective-upgrade-allowed semantics:
    //
    //     effectiveUpgradeAllowed =
    //         manga.UpgradeAllowedOverride
    //         ?? (translationProfile.UpgradeAllowed && customFormatProfile.UpgradeAllowed)
    //
    //   * manga.UpgradeAllowedOverride NULL  → fall back to per-profile flags AND-merged
    //   * manga.UpgradeAllowedOverride TRUE  → force-allow (per-profile flags ignored)
    //   * manga.UpgradeAllowedOverride FALSE → force-disallow (per-profile flags ignored)
    //
    // The AND-merge per-profile (rather than OR) is intentional: TranslationProfile defaults TRUE
    // and CustomFormatProfile defaults FALSE, so out-of-the-box manga gets language-rank upgrades
    // but NOT CF-score upgrades unless an admin explicitly enables.
    //
    // Comparison ordering (D-08 mirror): Language rank → CF score. Larger rank value = WORSE
    // language match (index in TranslationProfile.Languages — lower index = better preferred).
    // Larger CF score = BETTER. We accept the incoming as an upgrade when its rank is strictly
    // lower (better) OR same-rank-with-strictly-higher-CF-score.
    //
    // Phase 8 cleanup: collapse with TV UpgradeSpecification when Tv/ deletes.
    public class UpgradeSpecification : IMangaImportDecisionEngineSpecification
    {
        private readonly IChapterFileService _chapterFileService;
        private readonly ITranslationProfileService _translationProfileService;
        private readonly ICustomFormatProfileService _customFormatProfileService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public UpgradeSpecification(IChapterFileService chapterFileService,
                                    ITranslationProfileService translationProfileService,
                                    ICustomFormatProfileService customFormatProfileService,
                                    ICustomFormatCalculationService formatCalculator,
                                    IConfigService configService,
                                    Logger logger)
        {
            _chapterFileService = chapterFileService;
            _translationProfileService = translationProfileService;
            _customFormatProfileService = customFormatProfileService;
            _formatCalculator = formatCalculator;
            _configService = configService;
            _logger = logger;
        }

        public MangaImportSpecDecision IsSatisfiedBy(LocalChapter localChapter, DownloadClientItem downloadClientItem)
        {
            if (localChapter.ExistingFile)
            {
                return MangaImportSpecDecision.Accept();
            }

            if (localChapter.Chapter == null || localChapter.Manga == null)
            {
                return MangaImportSpecDecision.Accept();
            }

            var existingFiles = _chapterFileService.GetFilesByChapter(localChapter.Chapter.Id);
            if (existingFiles == null || existingFiles.Count == 0)
            {
                // No existing file — first import is always allowed regardless of upgrade flags.
                return MangaImportSpecDecision.Accept();
            }

            var translationProfile = ResolveTranslationProfile(localChapter);
            var customFormatProfile = ResolveCustomFormatProfile(localChapter);

            // D-10 three-state fallback: per-Manga override wins; otherwise AND-merge per-profile flags.
            var effectiveUpgradeAllowed = localChapter.Manga.UpgradeAllowedOverride
                ?? ((translationProfile?.UpgradeAllowed ?? true) && (customFormatProfile?.UpgradeAllowed ?? false));

            if (!effectiveUpgradeAllowed)
            {
                _logger.Debug(
                    "Upgrade not allowed for chapter {0} (manga.Override={1}, translation.UpgradeAllowed={2}, cf.UpgradeAllowed={3})",
                    localChapter.Chapter.Id,
                    localChapter.Manga.UpgradeAllowedOverride,
                    translationProfile?.UpgradeAllowed,
                    customFormatProfile?.UpgradeAllowed);

                return MangaImportSpecDecision.Reject(ImportRejectionReason.NotUpgradeAllowed,
                    "Upgrade not allowed by profile/manga override");
            }

            // Compare the incoming candidate vs each existing ChapterFile. If any existing file is
            // at-least-as-good (same rank with same-or-better CF score), reject.
            foreach (var existingFile in existingFiles)
            {
                var rankCompare = CompareLanguageRank(existingFile.TranslatedLanguage, localChapter.TranslatedLanguage, translationProfile);

                if (rankCompare < 0)
                {
                    // Existing has a LOWER (better) rank — incoming is a downgrade.
                    _logger.Debug(
                        "Existing chapter file {0} is a better language match; rejecting incoming",
                        existingFile.Path);
                    return MangaImportSpecDecision.Reject(
                        ImportRejectionReason.NotUpgrade,
                        "Existing chapter file is a better language match (existing={0}, incoming={1})",
                        existingFile.TranslatedLanguage ?? "(none)",
                        localChapter.TranslatedLanguage ?? "(none)");
                }

                if (rankCompare == 0)
                {
                    // Same language rank — fall through to CF score comparison.
                    var existingScore = ComputeExistingFileCustomFormatScore(existingFile, localChapter, customFormatProfile);
                    var incomingScore = localChapter.CustomFormatScore;

                    if (incomingScore <= existingScore)
                    {
                        _logger.Debug(
                            "Incoming CF score ({0}) does not improve on existing ({1}) at same language rank; rejecting",
                            incomingScore,
                            existingScore);
                        return MangaImportSpecDecision.Reject(
                            ImportRejectionReason.NotCustomFormatUpgrade,
                            "Existing chapter file is at least as good (same language; existing CF score {0} >= incoming {1})",
                            existingScore,
                            incomingScore);
                    }
                }

                // rankCompare > 0 — incoming has BETTER (lower-index) rank. Accept.
            }

            return MangaImportSpecDecision.Accept();
        }

        // Returns: -1 if existing has lower (better) rank index; 0 if equal; +1 if existing has worse rank.
        // Mirrors MangaDownloadDecisionComparer.CompareLanguageRank semantics. Unranked = int.MaxValue.
        private int CompareLanguageRank(string existingLanguage, string incomingLanguage, TranslationProfile profile)
        {
            var existingRank = ResolveRank(existingLanguage, profile);
            var incomingRank = ResolveRank(incomingLanguage, profile);
            return existingRank.CompareTo(incomingRank);
        }

        private static int ResolveRank(string language, TranslationProfile profile)
        {
            if (profile?.Languages == null || language.IsNullOrWhiteSpace())
            {
                return int.MaxValue;
            }

            var index = profile.Languages.FindIndex(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? int.MaxValue : index;
        }

        private TranslationProfile ResolveTranslationProfile(LocalChapter lc)
        {
            var profileId = lc.Manga?.TranslationProfileId ?? _configService.DefaultTranslationProfileId;
            return profileId == null ? null : _translationProfileService.Get(profileId.Value);
        }

        private CustomFormatProfile ResolveCustomFormatProfile(LocalChapter lc)
        {
            var profileId = lc.Manga?.CustomFormatProfileId ?? _configService.DefaultCustomFormatProfileId;
            return profileId == null ? null : _customFormatProfileService.Get(profileId.Value);
        }

        // Compute the existing ChapterFile's CF score against the CustomFormatProfile by
        // routing it through the canonical CustomFormatCalculationService.ParseCustomFormat
        // overload that accepts MangaCustomFormatInput (Phase 5 manga path). Reconstruct
        // a minimal ReleaseInfo from the ChapterFile's stored provenance (TranslatedLanguage
        // + ScanlationGroup) — these are the only fields the existing manga CF specs read
        // (Phase 5 D-09: TranslatedLanguageSpecification + ScanlationGroupSpecification +
        // SourceKeySpecification + ChapterTypeSpecification).
        private int ComputeExistingFileCustomFormatScore(ChapterFile existingFile, LocalChapter incoming, CustomFormatProfile profile)
        {
            if (profile == null || profile.FormatItems == null || profile.FormatItems.Count == 0)
            {
                return 0;
            }

            var release = new NzbDrone.Core.Parser.Model.ReleaseInfo
            {
                Title = existingFile.GetSceneOrFileName(),
                TranslatedLanguage = existingFile.TranslatedLanguage,
                ScanlationGroup = existingFile.ScanlationGroup
            };

            var input = new MangaCustomFormatInput
            {
                Filename = existingFile.GetSceneOrFileName(),
                Manga = incoming.Manga,
                Release = release,
                ChapterInfo = incoming.ParsedChapterInfo
            };

            var matched = _formatCalculator.ParseCustomFormat(input);
            return profile.CalculateCustomFormatScore(matched);
        }
    }
}
