using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.IndexerSearchTests.Manga
{
    // Phase 6 Plan 06-06 Task 2 — MangaReleaseSearchService fan-out behaviors.
    // Verifies: indexer Protocol filter (Http only), per-indexer try/catch isolation,
    // decision-maker pipeline.
    [TestFixture]
    public class MangaReleaseSearchServiceFixture : CoreTest<MangaReleaseSearchService>
    {
        private MangaSearchCriteria _mangaCriteria;
        private ChapterSearchCriteria _chapterCriteria;

        [SetUp]
        public void Setup()
        {
            _mangaCriteria = new MangaSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = 7, Title = "Test Manga" },
                Chapters = new List<NzbDrone.Core.Manga.Chapter>(),
                MonitoredChaptersOnly = true,
                UserInvokedSearch = false
            };

            _chapterCriteria = new ChapterSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = 7, Title = "Test Manga" },
                Chapters = new List<NzbDrone.Core.Manga.Chapter>
                {
                    new NzbDrone.Core.Manga.Chapter { Id = 100, MangaId = 7, ChapterNumber = 1m, TranslatedLanguage = "en" }
                }
            };

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Setup(d => d.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<MangaSearchCriteriaBase>()))
                .Returns((List<ReleaseInfo> reports, MangaSearchCriteriaBase _) =>
                    reports.Select(r => new MangaDownloadDecision(new RemoteChapter { Release = r })).ToList());
        }

        private Mock<IIndexer> BuildHttpIndexer(int id, string name, IList<ReleaseInfo> result = null, Exception throws = null)
        {
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Protocol).Returns(DownloadProtocol.Http);
            indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition { Id = id, Name = name });
            if (throws != null)
            {
                indexer.Setup(i => i.Fetch(It.IsAny<MangaSearchCriteria>())).ThrowsAsync(throws);
                indexer.Setup(i => i.Fetch(It.IsAny<ChapterSearchCriteria>())).ThrowsAsync(throws);
            }
            else
            {
                indexer.Setup(i => i.Fetch(It.IsAny<MangaSearchCriteria>())).ReturnsAsync(result ?? new List<ReleaseInfo>());
                indexer.Setup(i => i.Fetch(It.IsAny<ChapterSearchCriteria>())).ReturnsAsync(result ?? new List<ReleaseInfo>());
            }

            return indexer;
        }

        private Mock<IIndexer> BuildUsenetIndexer(int id, string name)
        {
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Protocol).Returns(DownloadProtocol.Usenet);
            indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition { Id = id, Name = name });
            return indexer;
        }

        [Test]
        public async Task MangaSearch_returns_empty_when_no_indexers_available()
        {
            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.AutomaticSearchEnabled(true))
                .Returns(new List<IIndexer>());

            var result = await Subject.MangaSearch(_mangaCriteria);

            result.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public async Task MangaSearch_filters_to_Http_protocol_indexers_only()
        {
            var http = BuildHttpIndexer(1, "MangaDex", new List<ReleaseInfo> { new() { Title = "Manga 001", Guid = "g1" } });
            var usenet = BuildUsenetIndexer(2, "Newznab");

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.AutomaticSearchEnabled(true))
                .Returns(new List<IIndexer> { http.Object, usenet.Object });

            await Subject.MangaSearch(_mangaCriteria);

            http.Verify(i => i.Fetch(It.IsAny<MangaSearchCriteria>()), Times.Once);
            usenet.Verify(i => i.Fetch(It.IsAny<MangaSearchCriteria>()), Times.Never);
        }

        [Test]
        public async Task MangaSearch_isolates_one_failing_indexer_so_batch_continues()
        {
            var good = BuildHttpIndexer(1, "MangaDex", new List<ReleaseInfo> { new() { Title = "Good 001", Guid = "g1" } });
            var bad = BuildHttpIndexer(2, "Comix", throws: new InvalidOperationException("simulated indexer crash"));

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.AutomaticSearchEnabled(true))
                .Returns(new List<IIndexer> { good.Object, bad.Object });

            var decisions = await Subject.MangaSearch(_mangaCriteria);

            decisions.Should().HaveCount(1);
            decisions.Single().RemoteChapter.Release.Title.Should().Be("Good 001");
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public async Task MangaSearch_pipes_aggregated_reports_into_decision_maker()
        {
            var indexer = BuildHttpIndexer(1, "MangaDex", new List<ReleaseInfo>
            {
                new() { Title = "A", Guid = "g1" },
                new() { Title = "B", Guid = "g2" }
            });

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.AutomaticSearchEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            var decisions = await Subject.MangaSearch(_mangaCriteria);

            decisions.Should().HaveCount(2);
            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Verify(d => d.GetSearchDecision(It.Is<List<ReleaseInfo>>(r => r.Count == 2), _mangaCriteria), Times.Once);
        }

        [Test]
        public async Task ChapterSearch_filters_to_Http_protocol_indexers_only()
        {
            var http = BuildHttpIndexer(1, "MangaDex", new List<ReleaseInfo> { new() { Title = "Ch1", Guid = "g1" } });
            var usenet = BuildUsenetIndexer(2, "Newznab");

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.AutomaticSearchEnabled(true))
                .Returns(new List<IIndexer> { http.Object, usenet.Object });

            await Subject.ChapterSearch(_chapterCriteria);

            http.Verify(i => i.Fetch(It.IsAny<ChapterSearchCriteria>()), Times.Once);
            usenet.Verify(i => i.Fetch(It.IsAny<ChapterSearchCriteria>()), Times.Never);
        }
    }
}
