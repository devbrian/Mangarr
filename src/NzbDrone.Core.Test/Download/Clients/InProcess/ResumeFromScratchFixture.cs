using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

// Sonarr divergence: Phase 15 Plan 15-11 cascade absorption — RemoteChapter/Series/Episode replaced with RemoteChapter/Manga/Chapter.

namespace NzbDrone.Core.Test.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 plan 04-03 Task 2 — DOWNLOAD-04 contract: process restart resumes from last
    /// completed page (D-05). The orchestrator's pre-resume scan reads the existing scratch
    /// directory, identifies pages already on disk by NNNN-padded filename, and skips them
    /// in the producer pipeline.
    /// </summary>
    [TestFixture]
    public class ResumeFromScratchFixture : CoreTest<ChapterDownloadService>
    {
        private Mock<IChapterDownloadStateRepository> _stateRepo;
        private Mock<IChapterPageFetcher> _fetcher;
        private Mock<IChapterArchiverFactory> _archiverFactory;
        private Mock<IChapterArchiver> _archiver;
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
            _scratchRoot = Path.Combine(Path.GetTempPath(), "rs-scratch-" + Guid.NewGuid().ToString("N"));
            _stagingRoot = Path.Combine(Path.GetTempPath(), "rs-staging-" + Guid.NewGuid().ToString("N"));

            _stateRepo = Mocker.GetMock<IChapterDownloadStateRepository>();
            _stateRepo.Setup(r => r.Insert(It.IsAny<ChapterDownloadState>()))
                      .Returns<ChapterDownloadState>(row =>
                      {
                          row.Id = ++_idSeed;
                          _persisted.Add(row);
                          return row;
                      });

            _fetcher = Mocker.GetMock<IChapterPageFetcher>();
            _fetcher.Setup(f => f.FetchPageBytesAsync(It.IsAny<IHttpAggregator>(), It.IsAny<ReleaseInfo>(), It.IsAny<ChapterPage>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new byte[] { 0xFF, 0xD8 });

            _archiver = new Mock<IChapterArchiver>();
            _archiver.SetupGet(a => a.FormatKey).Returns("cbz");
            _archiver.Setup(a => a.ArchiveAsync(It.IsAny<ChapterArchiveRequest>(), It.IsAny<CancellationToken>()))
                     .Returns((ChapterArchiveRequest req, CancellationToken _) => Task.FromResult(Path.Combine(req.StagingDir, req.OutputFilename + ".cbz")));

            _archiverFactory = Mocker.GetMock<IChapterArchiverFactory>();
            _archiverFactory.Setup(f => f.Resolve(It.IsAny<string>())).Returns(_archiver.Object);

            Mocker.GetMock<IEventAggregator>();

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
        public async Task Restart_skips_pages_already_on_disk()
        {
            // Pre-seed scratch dir with pages 1..3 of 5 already on disk (under the row Id assigned at insert).
            // The orchestrator's ScanExistingPageIndexes will find the NNNN-padded filenames and skip those page indices.

            var aggregator = BuildAggregator("rs-1");
            var settings = new InProcessImageDownloadClientSettings { DownloadsPerSource = 1, PagesPerChapter = 1 };

            // First-run insert callback mutates the row Id to 1; we'll pre-create scratch/1/0001..0003.jpg right BEFORE row 1 enqueues by intercepting Insert.
            _stateRepo.Setup(r => r.Insert(It.IsAny<ChapterDownloadState>()))
                      .Returns<ChapterDownloadState>(row =>
                      {
                          row.Id = ++_idSeed;
                          _persisted.Add(row);

                          // Pre-seed pages 1..3 BEFORE the orchestrator scans (CreateFolder runs immediately after Insert returns).
                          var rowScratch = Path.Combine(_scratchRoot, row.Id.ToString());
                          Directory.CreateDirectory(rowScratch);
                          for (var i = 1; i <= 3; i++)
                          {
                              File.WriteAllBytes(Path.Combine(rowScratch, $"{i:D4}.jpg"), new byte[] { 0xFF, 0xD8 });
                          }

                          return row;
                      });

            var rowId = await Subject.EnqueueAsync(BuildRemote(1, 100), aggregator.Object, BuildManifest(5), settings);

            await WaitUntil(() => _persisted.Single(r => r.Id == rowId).Status == ChapterDownloadStatus.Completed, TimeSpan.FromSeconds(10));

            // Pages 1..3 were on disk → fetcher called for pages 4 + 5 only (≤ 2 invocations).
            _fetcher.Verify(
                f => f.FetchPageBytesAsync(It.IsAny<IHttpAggregator>(), It.IsAny<ReleaseInfo>(), It.IsAny<ChapterPage>(), It.IsAny<CancellationToken>()),
                Times.AtMost(2),
                "resume must skip the 3 pages already on disk");
        }

        [Test]
        public async Task Resume_does_not_pre_emptively_re_fetch_manifest()
        {
            // Phase 4 v1 (CONTEXT.md <domain> §4 + plan 04-03 Task 2 NOTE): on resume the orchestrator
            // does NOT pre-emptively re-fetch a manifest; it scans scratch + queues remaining pages.
            // Re-fetch is REACTIVE on 403/410 only.

            var aggregator = BuildAggregator("rs-2");
            aggregator.Setup(a => a.GetChapterPages(It.IsAny<ReleaseInfo>()))
                      .ReturnsAsync(BuildManifest(2));

            var rowId = await Subject.EnqueueAsync(BuildRemote(1, 100), aggregator.Object, BuildManifest(2), new InProcessImageDownloadClientSettings { DownloadsPerSource = 1, PagesPerChapter = 1 });

            await WaitUntil(() => _persisted.Single(r => r.Id == rowId).Status == ChapterDownloadStatus.Completed, TimeSpan.FromSeconds(10));

            aggregator.Verify(
                a => a.GetChapterPages(It.IsAny<ReleaseInfo>()),
                Times.Never,
                "happy path must not re-fetch — re-fetch is REACTIVE on 403/410 only");
        }
    }
}
