using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Mangarr.Api.V5.Manga.Release;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Manga.Release
{
    // quick-260608-j33 ordering regression fixture. Proves MangaReleaseController.GetReleases
    // routes decisions through OrderForDisplay (which reuses the REAL MangaDownloadDecisionComparer
    // — the auto-download priority comparer) BEFORE MapDecisions assigns ReleaseWeight, so the
    // interactive-search default order equals the order the engine would grab in:
    //   * single-chapter (?chapterId): best-first by the comparer (download priority);
    //   * whole-manga   (?mangaId):    grouped by chapter number ascending, then priority within.
    // ReleaseWeight (0 = top) encodes that order; the unchanged frontend `releaseWeight`-ascending
    // default renders it. The tests fail if the controller reverts to unprioritized insertion-order
    // weights or drops the chapter grouping.
    //
    // Fixture lives under NzbDrone.Api.Test (not NzbDrone.Core.Test) because Mangarr.Core.Test does
    // not project-reference Mangarr.Api.V5 — same convention + harness as the sibling
    // MangaReleaseControllerDownloadFixture.cs (the ICacheManager.GetCache<RemoteChapter> ctor mock
    // is required because the controller resolves it in its ctor).
    //
    // A REAL MangaDownloadDecisionComparer is injected via Mocker.SetConstant so the genuine
    // ordering logic runs. With the mocked IConfigService.DefaultTranslationProfileId returning null
    // and the mocked ITranslationProfileService returning null, every release ties at language rank
    // (int.MaxValue), so CustomFormatScore (the comparer's second key) is the deterministic
    // discriminator the tests assert on.
    [TestFixture]
    public class MangaReleaseControllerSortFixture : TestBase<MangaReleaseController>
    {
        private Mock<ICached<RemoteChapter>> _cache;

        [SetUp]
        public void Setup()
        {
            // The controller resolves cacheManager.GetCache<RemoteChapter>(GetType(), "remoteChapters")
            // in its ctor; return a mock cache so MapDecisions' _remoteChapterCache.Set succeeds.
            _cache = new Mock<ICached<RemoteChapter>>();
            Mocker.GetMock<ICacheManager>()
                  .Setup(m => m.GetCache<RemoteChapter>(It.IsAny<System.Type>(), It.IsAny<string>()))
                  .Returns(_cache.Object);

            // Inject a REAL comparer so the genuine ordering logic runs (AutoMoq cannot mock the
            // concrete comparer meaningfully). Mocked deps make every release tie at language rank,
            // so CustomFormatScore becomes the discriminator.
            Mocker.SetConstant(new MangaDownloadDecisionComparer(
                Mocker.GetMock<ITranslationProfileService>().Object,
                Mocker.GetMock<IConfigService>().Object));
        }

        private static MangaDownloadDecision MakeDecision(decimal chapterNumber, int customFormatScore, string guid)
        {
            var remoteChapter = new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = 1 },
                Chapters = new List<NzbDrone.Core.Manga.Chapter>
                {
                    // Unique Id per chapter so cache keys / weights stay distinct.
                    new() { Id = guid.GetHashCode() & 0x7fffffff, ChapterNumber = chapterNumber }
                },
                Release = new NzbDrone.Core.Parser.Model.ReleaseInfo { Guid = guid, IndexerId = 1, Title = guid },
                CustomFormatScore = customFormatScore
            };

            // No rejections → Approved.
            return new MangaDownloadDecision(remoteChapter);
        }

        // Test A — chapter search orders by download priority (descending CustomFormatScore).
        [Test]
        public async Task ChapterSearch_should_order_resources_by_download_priority()
        {
            Mocker.GetMock<IChapterService>()
                  .Setup(s => s.GetChapter(It.IsAny<int>()))
                  .Returns(new NzbDrone.Core.Manga.Chapter { Id = 10, MangaId = 1 });

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetManga(1))
                  .Returns(new NzbDrone.Core.Manga.Manga { Id = 1 });

            // Scrambled CF order: 10 ("c"), 30 ("a"), 20 ("b") — all chapter 5.
            Mocker.GetMock<IMangaSearchForReleases>()
                  .Setup(s => s.ChapterSearch(It.IsAny<ChapterSearchCriteria>()))
                  .ReturnsAsync(new List<MangaDownloadDecision>
                  {
                      MakeDecision(5m, 10, "c"),
                      MakeDecision(5m, 30, "a"),
                      MakeDecision(5m, 20, "b"),
                  });

            var result = await Subject.GetReleases(chapterId: 10, mangaId: null);
            var ok = result.Result as Ok<List<MangaReleaseResource>>;
            ok.Should().NotBeNull();

            var resources = ok!.Value!;
            resources.Should().HaveCount(3);

            // Descending CustomFormatScore: 30, 20, 10.
            resources[0].CustomFormatScore.Should().Be(30);
            resources[1].CustomFormatScore.Should().Be(20);
            resources[2].CustomFormatScore.Should().Be(10);

            // ReleaseWeight encodes the final order, 0 = top.
            resources[0].ReleaseWeight.Should().Be(0);
            resources[1].ReleaseWeight.Should().Be(1);
            resources[2].ReleaseWeight.Should().Be(2);
        }

        // Test B — whole-manga search groups by chapter ascending, then priority within chapter.
        [Test]
        public async Task MangaSearch_should_group_by_chapter_then_priority()
        {
            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetManga(1))
                  .Returns(new NzbDrone.Core.Manga.Manga { Id = 1 });

            Mocker.GetMock<IChapterService>()
                  .Setup(s => s.GetChaptersByManga(1))
                  .Returns(new List<NzbDrone.Core.Manga.Chapter>());

            // Scrambled across two chapters: ch2/CF40, ch1/CF10, ch2/CF20, ch1/CF50.
            Mocker.GetMock<IMangaSearchForReleases>()
                  .Setup(s => s.MangaSearch(It.IsAny<MangaSearchCriteria>()))
                  .ReturnsAsync(new List<MangaDownloadDecision>
                  {
                      MakeDecision(2m, 40, "ch2-cf40"),
                      MakeDecision(1m, 10, "ch1-cf10"),
                      MakeDecision(2m, 20, "ch2-cf20"),
                      MakeDecision(1m, 50, "ch1-cf50"),
                  });

            var result = await Subject.GetReleases(chapterId: null, mangaId: 1);
            var ok = result.Result as Ok<List<MangaReleaseResource>>;
            ok.Should().NotBeNull();

            var resources = ok!.Value!;
            resources.Should().HaveCount(4);

            // Chapter ascending, then CF descending within chapter:
            // ch1/CF50, ch1/CF10, ch2/CF40, ch2/CF20.
            resources[0].CustomFormatScore.Should().Be(50);
            resources[1].CustomFormatScore.Should().Be(10);
            resources[2].CustomFormatScore.Should().Be(40);
            resources[3].CustomFormatScore.Should().Be(20);

            // The chapter number is mapped onto MappedEpisodeNumbers[0].
            resources[0].MappedEpisodeNumbers[0].Should().Be(1);
            resources[1].MappedEpisodeNumbers[0].Should().Be(1);
            resources[2].MappedEpisodeNumbers[0].Should().Be(2);
            resources[3].MappedEpisodeNumbers[0].Should().Be(2);

            // ReleaseWeight encodes the final flattened order, 0 = top.
            resources[0].ReleaseWeight.Should().Be(0);
            resources[1].ReleaseWeight.Should().Be(1);
            resources[2].ReleaseWeight.Should().Be(2);
            resources[3].ReleaseWeight.Should().Be(3);
        }
    }
}
