using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 plan 04-03 Task 1 — Wave 0 fixture asserting the IDownloadClient contract for the
    /// in-process image download client (DOWNLOAD-01 / DOWNLOAD-06) plus the Pitfall 8 / F-01
    /// regression guard that <c>Download()</c> ACTUALLY CALLS <c>aggregator.GetChapterPages()</c>
    /// (mirrors Phase 3 SONARR-AUDIT.md F-01 class — DI-injected ≠ wired).
    /// </summary>
    [TestFixture]
    public class InProcessImageDownloadClientFixture : CoreTest<InProcessImageDownloadClient>
    {
        private Mock<IHttpAggregator> _aggregator;
        private Mock<IChapterDownloadStateRepository> _stateRepo;
        private Mock<IChapterDownloadService> _orchestrator;
        private InProcessImageDownloadClientSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _aggregator = new Mock<IHttpAggregator>();
            _aggregator.As<IIndexer>();   // production indexer derives from HttpAggregatorBase which is BOTH IIndexer and IHttpAggregator.
            _aggregator.SetupGet(a => a.SourceKey).Returns("mangadex");
            _aggregator.Setup(a => a.GetChapterPages(It.IsAny<ReleaseInfo>()))
                       .ReturnsAsync(new ChapterManifest
                       {
                           Pages = new[] { new ChapterPage { Url = "https://x/1.jpg", PageIndex = 1 } },
                           TotalCount = 1
                       });

            _stateRepo = Mocker.GetMock<IChapterDownloadStateRepository>();
            _orchestrator = Mocker.GetMock<IChapterDownloadService>();
            _orchestrator.Setup(o => o.EnqueueAsync(
                            It.IsAny<RemoteEpisode>(),
                            It.IsAny<IHttpAggregator>(),
                            It.IsAny<ChapterManifest>(),
                            It.IsAny<InProcessImageDownloadClientSettings>()))
                         .ReturnsAsync(42);

            _settings = new InProcessImageDownloadClientSettings { DownloadsPerSource = 2, PagesPerChapter = 4, RetentionDays = 7 };

            // Provider definition + Settings (DownloadClientBase reads via Definition.Settings).
            Subject.Definition = new DownloadClientDefinition
            {
                Id = 1,
                Name = "Test In-Process",
                Settings = _settings,
                ConfigContract = nameof(InProcessImageDownloadClientSettings)
            };
        }

        [Test]
        public void Protocol_is_Http()
        {
            Subject.Protocol.Should().Be(DownloadProtocol.Http);
        }

        [Test]
        public void Name_is_canonical()
        {
            Subject.Name.Should().Be("Mangarr In-Process Downloader");
        }

        [Test]
        public async Task Download_invokes_GetChapterPages_via_indexerFactory()
        {
            // Pitfall 8 / F-01 mitigation — assert the consumer ACTUALLY calls the injected service.
            var release = new ReleaseInfo { IndexerId = 7, DownloadUrl = "https://x" };
            var remote = new RemoteEpisode { Release = release };

            var downloadId = await Subject.Download(remote, (IIndexer)_aggregator.Object);

            _aggregator.Verify(
                a => a.GetChapterPages(release),
                Times.Once,
                "Download() must call aggregator.GetChapterPages — Phase 3 F-01 class regression guard");
            _orchestrator.Verify(
                o => o.EnqueueAsync(remote, _aggregator.Object, It.IsAny<ChapterManifest>(), _settings),
                Times.Once);
            downloadId.Should().Be("42");
        }

        [Test]
        public async Task Download_throws_when_indexer_is_not_HttpAggregator()
        {
            var nonHttpIndexer = new Mock<IIndexer>().Object;
            var remote = new RemoteEpisode { Release = new ReleaseInfo() };

            Func<Task> act = () => Subject.Download(remote, nonHttpIndexer);

            await act.Should().ThrowAsync<InvalidOperationException>()
                      .WithMessage("*HttpAggregatorBase*");
        }

        [Test]
        public void GetItems_projects_from_AllInFlight()
        {
            var rows = new[]
            {
                new ChapterDownloadState
                {
                    Id = 1, MangaId = 1, ChapterId = 100, Title = "Ch 1",
                    Status = ChapterDownloadStatus.Downloading, TotalPages = 10, CompletedPages = 5
                },
                new ChapterDownloadState
                {
                    Id = 2, MangaId = 1, ChapterId = 101, Title = "Ch 2",
                    Status = ChapterDownloadStatus.Completed, TotalPages = 8, CompletedPages = 8,
                    StagingPath = "/staging/ch2.cbz"
                }
            };
            _stateRepo.Setup(r => r.AllInFlight()).Returns(rows);

            var items = Subject.GetItems().ToList();

            items.Should().HaveCount(2);
            items[0].DownloadId.Should().Be("1");
            items[0].Status.Should().Be(DownloadItemStatus.Downloading);
            items[1].DownloadId.Should().Be("2");
            items[1].Status.Should().Be(DownloadItemStatus.Completed);
            items[1].OutputPath.ToString().Should().Contain("ch2.cbz");
        }

        [Test]
        public void RemoveItem_deletes_row_and_scratch_when_deleteData_true()
        {
            var row = new ChapterDownloadState { Id = 5, ScratchDir = "/scratch/5" };
            _stateRepo.Setup(r => r.Get(5)).Returns(row);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists("/scratch/5")).Returns(true);

            var item = new DownloadClientItem
            {
                DownloadId = "5",
                OutputPath = new OsPath(null)
            };
            Subject.RemoveItem(item, deleteData: true);

            _stateRepo.Verify(r => r.Delete(5), Times.Once);
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder("/scratch/5", true), Times.Once);
        }

        [Test]
        public void RemoveItem_preserves_scratch_when_deleteData_false()
        {
            var row = new ChapterDownloadState { Id = 6, ScratchDir = "/scratch/6" };
            _stateRepo.Setup(r => r.Get(6)).Returns(row);

            var item = new DownloadClientItem { DownloadId = "6", OutputPath = new OsPath(null) };
            Subject.RemoveItem(item, deleteData: false);

            _stateRepo.Verify(r => r.Delete(6), Times.Once);
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void Auto_discovered_via_provider_factory()
        {
            // Verify InProcessImageDownloadClient is structurally eligible for ThingiProvider auto-discovery.
            // The reflection scan picks up public classes extending DownloadClientBase<T> with a public ctor.
            typeof(InProcessImageDownloadClient).Should().BeAssignableTo<IDownloadClient>();
            typeof(InProcessImageDownloadClient).IsPublic.Should().BeTrue();
            typeof(InProcessImageDownloadClient).BaseType.Should().NotBeNull();
            typeof(InProcessImageDownloadClient).BaseType!.IsGenericType.Should().BeTrue();
            typeof(InProcessImageDownloadClient).BaseType!.GetGenericTypeDefinition().Should().Be(typeof(DownloadClientBase<>));
        }
    }
}
