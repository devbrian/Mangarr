using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.IndexerSearchTests.Manga
{
    // Phase 6 Plan 06-06 Task 3 — MangaRssSyncService Http filter + per-indexer SyncInterval.
    [TestFixture]
    public class MangaRssSyncServiceFixture : CoreTest<MangaRssSyncService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.MangaRssSyncInterval).Returns(15);

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                .Returns(new List<MangaDownloadDecision>());
        }

        private Mock<IIndexer> BuildHttpIndexer(
            int id,
            string name,
            IList<ReleaseInfo> result = null,
            Exception throws = null,
            int syncInterval = 0,
            DateTime? lastRssSync = null)
        {
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Protocol).Returns(DownloadProtocol.Http);
            indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition
            {
                Id = id,
                Name = name,
                SyncInterval = syncInterval,
                LastRssSync = lastRssSync
            });
            if (throws != null)
            {
                indexer.Setup(i => i.FetchRecent()).ThrowsAsync(throws);
            }
            else
            {
                indexer.Setup(i => i.FetchRecent()).ReturnsAsync(result ?? new List<ReleaseInfo>());
            }

            return indexer;
        }

        private Mock<IIndexer> BuildUsenetIndexer(int id, string name)
        {
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Protocol).Returns(DownloadProtocol.Usenet);
            indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition { Id = id, Name = name });
            indexer.Setup(i => i.FetchRecent()).ReturnsAsync(new List<ReleaseInfo>());
            return indexer;
        }

        [Test]
        public void Execute_filters_to_http_protocol_indexers_only()
        {
            var http = BuildHttpIndexer(1, "MangaDex");
            var usenet = BuildUsenetIndexer(2, "Newznab");

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { http.Object, usenet.Object });

            Subject.Execute(new MangaRssSyncCommand());

            http.Verify(i => i.FetchRecent(), Times.Once);
            usenet.Verify(i => i.FetchRecent(), Times.Never);
        }

        [Test]
        public void Execute_isolates_one_failing_indexer_so_batch_continues()
        {
            var good = BuildHttpIndexer(1, "MangaDex", new List<ReleaseInfo> { new() { Title = "Good 001", Guid = "g1" } });
            var bad = BuildHttpIndexer(2, "Comix", throws: new InvalidOperationException("simulated indexer crash"));

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { good.Object, bad.Object });

            Subject.Execute(new MangaRssSyncCommand());

            good.Verify(i => i.FetchRecent(), Times.Once);
            bad.Verify(i => i.FetchRecent(), Times.Once);
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void Execute_honors_per_indexer_sync_interval_override_and_skips_when_not_due()
        {
            // Indexer override = 30min; LastRssSync was 15min ago — NOT due.
            var notDue = BuildHttpIndexer(
                1,
                "MangaDex",
                syncInterval: 30,
                lastRssSync: DateTime.UtcNow.AddMinutes(-15));

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { notDue.Object });

            Subject.Execute(new MangaRssSyncCommand());

            notDue.Verify(i => i.FetchRecent(), Times.Never);
        }

        [Test]
        public void Execute_per_indexer_override_due_when_elapsed_exceeds_override()
        {
            // Indexer override = 5min; LastRssSync was 10min ago — due.
            var due = BuildHttpIndexer(
                1,
                "MangaDex",
                syncInterval: 5,
                lastRssSync: DateTime.UtcNow.AddMinutes(-10));

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { due.Object });

            Subject.Execute(new MangaRssSyncCommand());

            due.Verify(i => i.FetchRecent(), Times.Once);
        }

        [Test]
        public void Execute_treats_never_synced_indexer_as_due()
        {
            var fresh = BuildHttpIndexer(1, "MangaDex", lastRssSync: null);

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { fresh.Object });

            Subject.Execute(new MangaRssSyncCommand());

            fresh.Verify(i => i.FetchRecent(), Times.Once);
        }

        [Test]
        public void Execute_pipes_aggregated_reports_into_decision_maker()
        {
            var indexer = BuildHttpIndexer(1, "MangaDex", new List<ReleaseInfo>
            {
                new() { Title = "A", Guid = "g1" },
                new() { Title = "B", Guid = "g2" }
            });

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Verify(d => d.GetRssDecision(It.Is<List<ReleaseInfo>>(r => r.Count == 2), It.IsAny<bool>()),
                    Times.Once);
        }
    }
}
