using System.Collections.Generic;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests.Manga
{
    // Per Phase 5 plan 05-04 + 05-VALIDATION.md Wave 0 + pattern S9. Shared Mocker +
    // Build.A.RemoteChapter helpers consumed by every per-spec fixture and by
    // Wave 4's MangaDownloadDecisionMakerEndToEndFixture.
    //
    // Replaces the Wave 0 placeholder stub at this same path. The path is verbatim-stable
    // because Wave 4 (plan 05-07) consumes this base directly.
    public abstract class MangaDecisionEngineSpecFixtureBase<TSpec> : CoreTest<TSpec>
        where TSpec : class
    {
        protected RemoteChapter BuildRemoteChapter(
            string releaseLanguage = "en",
            int customFormatScore = 0,
            int? translationProfileId = null,
            int? customFormatProfileId = null,
            bool mangaMonitored = true,
            bool chapterMonitored = true,
            int chapterId = 100,
            int indexerPriority = 50,
            double ageHours = 24,
            long size = 10_000_000,
            int votes = 0)
        {
            return new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga
                {
                    Id = 1,
                    Monitored = mangaMonitored,
                    TranslationProfileId = translationProfileId,
                    CustomFormatProfileId = customFormatProfileId,
                    Title = "Test Manga"
                },
                Chapters = new List<Chapter>
                {
                    new() { Id = chapterId, Monitored = chapterMonitored, ChapterNumber = 42m }
                },
                Release = new ReleaseInfo
                {
                    Title = "Test - Chapter 042",
                    TranslatedLanguage = releaseLanguage,
                    IndexerPriority = indexerPriority,
                    PublishDate = System.DateTime.UtcNow.AddHours(-ageHours),
                    Size = size,
                    Votes = votes,
                    Indexer = "TestIndexer"
                },
                CustomFormats = new List<CustomFormat>(),
                CustomFormatScore = customFormatScore
            };
        }

        protected TranslationProfile BuildTranslationProfile(int id, IEnumerable<string> languages, bool allowOthers = false)
        {
            return new TranslationProfile
            {
                Id = id,
                Name = "TestProfile-" + id,
                Languages = new List<string>(languages),
                AllowLanguagesNotInProfile = allowOthers
            };
        }

        protected CustomFormatProfile BuildCustomFormatProfile(int id, int min = 0, int? max = null)
        {
            return new CustomFormatProfile
            {
                Id = id,
                Name = "TestCFProfile-" + id,
                MinFormatScore = min,
                MaxFormatScore = max,
                FormatItems = new List<ProfileFormatItem>()
            };
        }
    }
}
