using System.Collections.Generic;
using System.IO;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.CustomFormats;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit gap
    // `no-sibling/LocalEpisodeCustomFormatCalculationService` — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/LocalEpisodeCustomFormatCalculationService.cs
    //
    // ============================================================================
    // GAP CLOSED: LocalChapter.CustomFormatScore was reaching UpgradeSpecification
    // (and the LocalChapterUpgradeRankComparer in ImportApprovedChapters) as 0
    // (the default) without an upstream population step. Result: the
    // `incomingScore <= existingScore` upgrade gate at UpgradeSpecification.cs:124
    // always rejected when an existing ChapterFile had any non-zero CF score —
    // silently losing all CF-driven upgrade comparisons in the import pipeline.
    //
    // This is the manga sibling of TV's LocalEpisodeCustomFormatCalculationService:
    // it routes the LocalChapter through ICustomFormatCalculationService.ParseCustomFormat
    // (manga overload accepting MangaCustomFormatInput per Phase 5 D-09 / Pitfall 4)
    // and stamps the resulting CustomFormats + CustomFormatScore onto the LocalChapter
    // BEFORE specs evaluate.
    //
    // Manga divergences from TV:
    //   * No IBuildFileNames dependency — manga has no QualityProfile FK on the entity;
    //     the filename used for CF parsing is simply Path.GetFileName(localChapter.Path).
    //     This mirrors what UpgradeSpecification.ComputeExistingFileCustomFormatScore
    //     already does for the existing-file side (see UpgradeSpecification.cs:199 —
    //     `existingFile.GetSceneOrFileName()`).
    //   * No OriginalFileNameCustomFormats / OriginalFileNameCustomFormatScore —
    //     manga has a single canonical filename per LocalChapter (the staging CBZ),
    //     not the TV pair-of-names where the imported filename can differ from the
    //     scene release filename.
    //   * Score is computed via Manga.CustomFormatProfileId ?? Config.DefaultCustomFormatProfileId
    //     (Phase 5 D-07), not Series.QualityProfile. Mirrors MangaDownloadDecisionMaker.cs:179-189.
    //   * MangaCustomFormatInput is the input shape (Phase 5 D-09 sibling input);
    //     a minimal ReleaseInfo is reconstructed from the LocalChapter's resolved
    //     TranslatedLanguage + ScanlationGroup + (optionally) Release for the manga
    //     CF specs (TranslatedLanguageSpecification, ScanlationGroupSpecification,
    //     SourceKeySpecification, ChapterTypeSpecification — Phase 5 D-09).
    //
    // Phase 8 cleanup: collapse with TV LocalEpisodeCustomFormatCalculationService
    // when Tv/ deletes.
    // ============================================================================
    public interface ILocalChapterCustomFormatCalculationService
    {
        List<CustomFormat> ParseChapterCustomFormats(LocalChapter localChapter);
        void UpdateChapterCustomFormats(LocalChapter localChapter);
    }

    public class LocalChapterCustomFormatCalculationService : ILocalChapterCustomFormatCalculationService
    {
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly ICustomFormatProfileService _customFormatProfileService;
        private readonly IConfigService _configService;

        public LocalChapterCustomFormatCalculationService(
            ICustomFormatCalculationService formatCalculator,
            ICustomFormatProfileService customFormatProfileService,
            IConfigService configService)
        {
            _formatCalculator = formatCalculator;
            _customFormatProfileService = customFormatProfileService;
            _configService = configService;
        }

        public List<CustomFormat> ParseChapterCustomFormats(LocalChapter localChapter)
        {
            return _formatCalculator.ParseCustomFormat(BuildInput(localChapter));
        }

        public void UpdateChapterCustomFormats(LocalChapter localChapter)
        {
            if (localChapter == null)
            {
                return;
            }

            localChapter.CustomFormats = _formatCalculator.ParseCustomFormat(BuildInput(localChapter));

            var profile = ResolveCustomFormatProfile(localChapter);
            localChapter.CustomFormatScore = profile?.CalculateCustomFormatScore(localChapter.CustomFormats) ?? 0;
        }

        private MangaCustomFormatInput BuildInput(LocalChapter localChapter)
        {
            // Reconstruct a minimal ReleaseInfo from the LocalChapter's already-resolved
            // provenance fields. Mirrors UpgradeSpecification.ComputeExistingFileCustomFormatScore
            // (UpgradeSpecification.cs:190-195) — these are the only fields the manga CF
            // specs read (Phase 5 D-09).
            var release = localChapter.Release ?? new ReleaseInfo();
            release.TranslatedLanguage ??= localChapter.TranslatedLanguage;
            release.ScanlationGroup ??= localChapter.ScanlationGroup;

            var filename = string.IsNullOrWhiteSpace(localChapter.Path)
                ? null
                : Path.GetFileName(localChapter.Path);

            return new MangaCustomFormatInput
            {
                Filename = filename,
                Manga = localChapter.Manga,
                Release = release,
                ChapterInfo = localChapter.ParsedChapterInfo,
                Size = localChapter.Size
            };
        }

        private CustomFormatProfile ResolveCustomFormatProfile(LocalChapter localChapter)
        {
            // Phase 5 D-07 fallback: per-Manga FK ?? global default. Mirrors
            // MangaDownloadDecisionMaker.cs:179 + UpgradeSpecification.cs:172.
            var profileId = localChapter.Manga?.CustomFormatProfileId ?? _configService.DefaultCustomFormatProfileId;
            return profileId.HasValue ? _customFormatProfileService.Get(profileId.Value) : null;
        }
    }
}
