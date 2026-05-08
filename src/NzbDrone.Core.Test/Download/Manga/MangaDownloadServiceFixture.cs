using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Manga
{
    // Phase 15 Wave (A) pre-land per
    // .planning/phases/15-domain-rename-rebrand/15-CONTRACTS-AUDIT.md §8 (the single
    // NEW contract). Mirrors src/NzbDrone.Core.Test/Download/DownloadServiceFixture.cs
    // shape, with manga DTOs (RemoteChapter / Manga / Chapter) substituted for
    // (RemoteEpisode / Series / Episode), and ChapterGrabbedEvent substituted for
    // EpisodeGrabbedEvent.
    [TestFixture]
    public class MangaDownloadServiceFixture : CoreTest<MangaDownloadService>
    {
        private RemoteChapter _remoteChapter;
        private List<IDownloadClient> _downloadClients;

        [SetUp]
        public void Setup()
        {
            _downloadClients = new List<IDownloadClient>();

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.GetDownloadClients(It.IsAny<DownloadProtocol>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                .Returns<DownloadProtocol, int, bool, HashSet<int>>((v, i, f, t) => _downloadClients.Where(d => d.Protocol == v));

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.GetDownloadClient(It.IsAny<DownloadProtocol>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                .Returns<DownloadProtocol, int, bool, HashSet<int>>((v, i, f, t) => _downloadClients.FirstOrDefault(d => d.Protocol == v));

            var manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 5)
                .With(m => m.Title = "Test Manga")
                .With(m => m.Tags = new HashSet<int>())
                .Build();

            var chapters = Builder<NzbDrone.Core.Manga.Chapter>.CreateListOfSize(2)
                .TheFirst(1).With(c => c.Id = 12).With(c => c.MangaId = 5)
                .TheNext(1).With(c => c.Id = 99).With(c => c.MangaId = 5)
                .Build().ToList();

            var releaseInfo = Builder<ReleaseInfo>.CreateNew()
                .With(v => v.DownloadProtocol = DownloadProtocol.Http)
                .With(v => v.DownloadUrl = "http://test.site/download1.cbz")
                .Build();

            _remoteChapter = new RemoteChapter
            {
                Manga = manga,
                Chapters = chapters,
                Release = releaseInfo
            };
        }

        private Mock<IDownloadClient> WithHttpClient()
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition).Returns(Builder<IndexerDefinition>.CreateNew().Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(DownloadProtocol.Http);

            return mock;
        }

        [Test]
        public async Task Download_report_should_publish_chapter_grabbed_event()
        {
            var mock = WithHttpClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteChapter>(), It.IsAny<IIndexer>()));

            await Subject.DownloadReport(_remoteChapter, null);

            VerifyEventPublished<ChapterGrabbedEvent>();
        }

        [Test]
        public async Task Download_report_should_grab_using_client()
        {
            var mock = WithHttpClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteChapter>(), It.IsAny<IIndexer>()));

            await Subject.DownloadReport(_remoteChapter, null);

            mock.Verify(s => s.Download(It.IsAny<RemoteChapter>(), It.IsAny<IIndexer>()), Times.Once());
        }

        [Test]
        public void Download_report_should_not_publish_event_on_failed_grab()
        {
            var mock = WithHttpClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteChapter>(), It.IsAny<IIndexer>()))
                .Throws(new WebException());

            Assert.ThrowsAsync<DownloadClientUnavailableException>(async () => await Subject.DownloadReport(_remoteChapter, null));

            VerifyEventNotPublished<ChapterGrabbedEvent>();
        }

        [Test]
        public void Download_report_should_trigger_indexer_backoff_on_indexer_error()
        {
            var mock = WithHttpClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteChapter>(), It.IsAny<IIndexer>()))
                .Callback<RemoteChapter, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseDownloadException(v.Release, "Error", new WebException());
                });

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_remoteChapter, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Once());
        }

        [Test]
        public void Should_throw_if_no_client_configured()
        {
            Assert.ThrowsAsync<DownloadClientUnavailableException>(async () => await Subject.DownloadReport(_remoteChapter, null));

            VerifyEventNotPublished<ChapterGrabbedEvent>();
        }

        [Test]
        public async Task Should_route_to_correct_protocol_client()
        {
            var http = WithHttpClient();

            await Subject.DownloadReport(_remoteChapter, null);

            http.Verify(c => c.Download(It.IsAny<RemoteChapter>(), It.IsAny<IIndexer>()), Times.Once());
        }

        [Test]
        public void Should_route_release_unavailable_to_throw_without_indexer_backoff()
        {
            var mock = WithHttpClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteChapter>(), It.IsAny<IIndexer>()))
                .Callback<RemoteChapter, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseUnavailableException(v.Release, "Error", new WebException());
                });

            Assert.ThrowsAsync<ReleaseUnavailableException>(async () => await Subject.DownloadReport(_remoteChapter, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        [Test]
        public async Task Specific_download_client_id_should_use_get_path()
        {
            var mock = WithHttpClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteChapter>(), It.IsAny<IIndexer>()));

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.Get(7))
                .Returns(mock.Object);

            await Subject.DownloadReport(_remoteChapter, downloadClientId: 7);

            Mocker.GetMock<IProvideDownloadClient>()
                .Verify(v => v.Get(7), Times.Once());
            mock.Verify(s => s.Download(It.IsAny<RemoteChapter>(), It.IsAny<IIndexer>()), Times.Once());
            VerifyEventPublished<ChapterGrabbedEvent>();
        }
    }
}
