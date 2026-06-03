using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

// Sonarr divergence: Phase 15 Plan 15-11 cascade absorption — RemoteChapter/Series/Episode replaced with RemoteChapter/Manga/Chapter.

namespace NzbDrone.Core.Test.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 plan 04-03 Task 2 — DOWNLOAD-05 contract: a single failing chapter must NOT block
    /// other chapters in the queue. Per-chapter try/catch envelope (Pattern 3) ensures the
    /// per-source worker Task survives unhandled exceptions inside <c>ProcessChapterAsync</c>.
    /// </summary>
    [TestFixture]
    public class FailureIsolationFixture : CoreTest<ChapterDownloadService>
    {
        private Mock<IChapterDownloadStateRepository> _stateRepo;
        private Mock<IChapterPageFetcher> _fetcher;
        private Mock<IChapterArchiverFactory> _archiverFactory;
        private Mock<IChapterArchiver> _archiver;
        private Mock<IEventAggregator> _eventAggregator;
        private Mock<IConfigService> _config;
        private Mock<IDiskProvider> _disk;
        private List<ChapterDownloadState> _persisted;
        private int _idSeed;
        private string _scratchRoot;
        private string _stagingRoot;

        [SetUp]
        public void SetUp()
        {
            _persisted = new List<ChapterDownloadState>();
            _idSeed = 0;
            _scratchRoot = Path.Combine(Path.GetTempPath(), "fi-scratch-" + Guid.NewGuid().ToString("N"));
            _stagingRoot = Path.Combine(Path.GetTempPath(), "fi-staging-" + Guid.NewGuid().ToString("N"));

            _stateRepo = Mocker.GetMock<IChapterDownloadStateRepository>();
            _stateRepo.Setup(r => r.Insert(It.IsAny<ChapterDownloadState>()))
                      .Returns<ChapterDownloadState>(row =>
                      {
                          row.Id = ++_idSeed;
                          _persisted.Add(row);
                          return row;
                      });

            _fetcher = Mocker.GetMock<IChapterPageFetcher>();

            _archiver = new Mock<IChapterArchiver>();
            _archiver.SetupGet(a => a.FormatKey).Returns("cbz");
            _archiver.Setup(a => a.ArchiveAsync(It.IsAny<ChapterArchiveRequest>(), It.IsAny<CancellationToken>()))
                     .Returns((ChapterArchiveRequest req, CancellationToken _) => Task.FromResult(Path.Combine(req.StagingDir, req.OutputFilename + ".cbz")));

            _archiverFactory = Mocker.GetMock<IChapterArchiverFactory>();
            _archiverFactory.Setup(f => f.Resolve(It.IsAny<string>())).Returns(_archiver.Object);

            _eventAggregator = Mocker.GetMock<IEventAggregator>();

            _config = Mocker.GetMock<IConfigService>();
            _config.SetupGet(c => c.DownloadScratchPath).Returns(_scratchRoot);
            _config.SetupGet(c => c.StagingPath).Returns(_stagingRoot);
            _config.SetupGet(c => c.OutputFormat).Returns("cbz");
            _config.SetupGet(c => c.RetentionDays).Returns(7);

            _disk = Mocker.GetMock<IDiskProvider>();
            _disk.Setup(d => d.CreateFolder(It.IsAny<string>()))
                 .Callback<string>(p => Directory.CreateDirectory(p));
            _disk.Setup(d => d.FolderExists(It.IsAny<string>()))
                 .Returns<string>(Directory.Exists);
            _disk.Setup(d => d.GetFiles(It.IsAny<string>(), It.IsAny<bool>()))
                 .Returns<string, bool>((p, _) => Directory.Exists(p)
                     ? Directory.GetFiles(p)
                     : Array.Empty<string>());
        }

        [TearDown]
        public void TearDown()
        {
            TryDeleteFolder(_scratchRoot);
            TryDeleteFolder(_stagingRoot);
        }

        private static void TryDeleteFolder(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Best-effort cleanup; ignore.
            }
        }

        private static Mock<IHttpAggregator> BuildAggregator(string sourceKey)
        {
            var m = new Mock<IHttpAggregator>();
            m.SetupGet(a => a.SourceKey).Returns(sourceKey);
            return m;
        }

        private static RemoteChapter BuildRemote(int mangaId, int chapterId)
        {
            return new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = mangaId, Title = $"M{mangaId}" },
                Chapters = new List<Chapter> { new Chapter { Id = chapterId } },
                Release = new ReleaseInfo { Title = $"Chapter {chapterId}", IndexerId = 1 }
            };
        }

        private static ChapterManifest BuildManifest(int pageCount)
        {
            var pages = Enumerable.Range(1, pageCount)
                .Select(i => new ChapterPage { Url = $"https://cdn/{i}.jpg", PageIndex = i, ContentTypeHint = "image/jpeg" })
                .ToArray();
            return new ChapterManifest { Pages = pages, ScanlationGroup = "G", TotalCount = pageCount };
        }

        private async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!predicate() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task Failed_chapter_does_not_block_queue()
        {
            // Chapter B's fetcher throws unconditionally; A and C succeed.
            // Queue: A, B, C — assert all three reach terminal status (B Failed; A,C Completed).
            var aggregator = BuildAggregator("mangadex");

            _fetcher.Setup(f => f.FetchPageBytesAsync(It.IsAny<IHttpAggregator>(), It.IsAny<ReleaseInfo>(), It.IsAny<ChapterPage>(), It.IsAny<CancellationToken>()))
                    .Returns<IHttpAggregator, ReleaseInfo, ChapterPage, CancellationToken>((_, release, _, _) =>
                    {
                        if (release.Title.EndsWith("B"))
                        {
                            throw new InvalidOperationException("simulated fetch failure for chapter B");
                        }

                        return Task.FromResult(new byte[] { 0xFF, 0xD8 });
                    });

            var settings = new InProcessImageDownloadClientSettings { DownloadsPerSource = 1, PagesPerChapter = 1 };

            // Note: we can't use BuildRemote here because we want titles A/B/C. Override release.
            async Task<int> Enqueue(string suffix)
            {
                var remote = new RemoteChapter
                {
                    Manga = new NzbDrone.Core.Manga.Manga { Id = 1, Title = "M" },
                    Chapters = new List<Chapter> { new Chapter { Id = 100 + suffix[0] } },
                    Release = new ReleaseInfo { Title = "Chapter " + suffix, IndexerId = 1 }
                };
                return await Subject.EnqueueAsync(remote, aggregator.Object, BuildManifest(1), settings);
            }

            var idA = await Enqueue("A");
            var idB = await Enqueue("B");
            var idC = await Enqueue("C");

            await WaitUntil(
                () => _persisted.Count == 3 && _persisted.All(r => r.Status == ChapterDownloadStatus.Completed || r.Status == ChapterDownloadStatus.Failed),
                TimeSpan.FromSeconds(15));

            _persisted.Single(r => r.Id == idA).Status.Should().Be(ChapterDownloadStatus.Completed);
            _persisted.Single(r => r.Id == idB).Status.Should().Be(ChapterDownloadStatus.Failed);
            _persisted.Single(r => r.Id == idC).Status.Should().Be(ChapterDownloadStatus.Completed);

            // Phase 39 RETIRE-01: the per-completion ChapterArchivedEvent assertion was removed
            // with the event type. A and C reaching Status=Completed (asserted above) is the
            // surviving completion signal; B's terminal failure still emits ChapterDownloadFailedEvent.
            _eventAggregator.Verify(
                e => e.PublishEvent(It.Is<ChapterDownloadFailedEvent>(ev => ev.RowId == idB)),
                Times.Once);

            ExceptionVerification.ExpectedErrors(1);   // chapter B's failure logs an error.
        }

        [Test]
        public async Task Single_failing_chapter_does_not_propagate_to_worker()
        {
            // Worker continues consuming after a failure; subsequent chapter still completes.
            var aggregator = BuildAggregator("mangadex");

            var callCount = 0;
            _fetcher.Setup(f => f.FetchPageBytesAsync(It.IsAny<IHttpAggregator>(), It.IsAny<ReleaseInfo>(), It.IsAny<ChapterPage>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        callCount++;
                        if (callCount == 1)
                        {
                            throw new InvalidOperationException("first chapter explosion");
                        }

                        return Task.FromResult(new byte[] { 0xFF, 0xD8 });
                    });

            var settings = new InProcessImageDownloadClientSettings { DownloadsPerSource = 1, PagesPerChapter = 1 };
            var idFail = await Subject.EnqueueAsync(BuildRemote(1, 100), aggregator.Object, BuildManifest(1), settings);
            var idGood = await Subject.EnqueueAsync(BuildRemote(1, 101), aggregator.Object, BuildManifest(1), settings);

            await WaitUntil(
                () => _persisted.Count == 2
                      && _persisted.Single(r => r.Id == idFail).Status == ChapterDownloadStatus.Failed
                      && _persisted.Single(r => r.Id == idGood).Status == ChapterDownloadStatus.Completed,
                TimeSpan.FromSeconds(10));

            _persisted.Single(r => r.Id == idFail).Status.Should().Be(ChapterDownloadStatus.Failed);
            _persisted.Single(r => r.Id == idGood).Status.Should().Be(ChapterDownloadStatus.Completed);

            ExceptionVerification.ExpectedErrors(1);   // first chapter's failure logs an error.
        }
    }
}
