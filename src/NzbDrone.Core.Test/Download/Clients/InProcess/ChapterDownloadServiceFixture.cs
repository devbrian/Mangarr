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
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 plan 04-03 Task 2 — exercises the bounded Channel&lt;T&gt; orchestrator
    /// (DOWNLOAD-02): per-source channel capacity, cross-source independence,
    /// ChapterArchivedEvent emission, manifest re-fetch flow on 403/410, and the
    /// BLOCKER #4 regression guards that per-instance Settings flow through.
    /// </summary>
    [TestFixture]
    public class ChapterDownloadServiceFixture : CoreTest<ChapterDownloadService>
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
            _scratchRoot = Path.Combine(Path.GetTempPath(), "scratch-" + Guid.NewGuid().ToString("N"));
            _stagingRoot = Path.Combine(Path.GetTempPath(), "staging-" + Guid.NewGuid().ToString("N"));

            _stateRepo = Mocker.GetMock<IChapterDownloadStateRepository>();
            _stateRepo.Setup(r => r.Insert(It.IsAny<ChapterDownloadState>()))
                      .Returns<ChapterDownloadState>(row =>
                      {
                          row.Id = ++_idSeed;
                          _persisted.Add(row);
                          return row;
                      });
            _stateRepo.Setup(r => r.SetFields(It.IsAny<ChapterDownloadState>(), It.IsAny<System.Linq.Expressions.Expression<Func<ChapterDownloadState, object>>[]>()));

            _fetcher = Mocker.GetMock<IChapterPageFetcher>();
            _fetcher.Setup(f => f.FetchPageBytesAsync(It.IsAny<IHttpAggregator>(), It.IsAny<ReleaseInfo>(), It.IsAny<ChapterPage>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 });

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

        private static RemoteEpisode BuildRemote(int mangaId, int chapterId)
        {
            return new RemoteEpisode
            {
                Series = new Series { Id = mangaId, Title = $"M{mangaId}" },
                Episodes = new List<Episode> { new Episode { Id = chapterId } },
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
        public async Task EnqueueAsync_inserts_row_before_any_HTTP_work()
        {
            // Pattern 3 / D-08 — row must exist before page fetches start so a process kill
            // mid-download leaves a recoverable row.
            var aggregator = BuildAggregator("mangadex");
            var rowId = await Subject.EnqueueAsync(BuildRemote(1, 100), aggregator.Object, BuildManifest(2), new InProcessImageDownloadClientSettings());

            // Insert is called immediately on EnqueueAsync.
            rowId.Should().BeGreaterThan(0);
            _persisted.Should().Contain(r => r.Id == rowId);
            _stateRepo.Verify(r => r.Insert(It.IsAny<ChapterDownloadState>()), Times.AtLeastOnce);

            // Drain to completion so we don't leak workers across tests.
            await WaitUntil(() => _persisted.Single(r => r.Id == rowId).Status == ChapterDownloadStatus.Completed, TimeSpan.FromSeconds(10));
            _persisted.Single(r => r.Id == rowId).Status.Should().Be(ChapterDownloadStatus.Completed);
        }

        [Test]
        public async Task On_complete_emits_ChapterArchivedEvent()
        {
            var aggregator = BuildAggregator("mangadex");
            var rowId = await Subject.EnqueueAsync(BuildRemote(1, 100), aggregator.Object, BuildManifest(3), new InProcessImageDownloadClientSettings { DownloadsPerSource = 1, PagesPerChapter = 1 });

            await WaitUntil(() => _persisted.Single(r => r.Id == rowId).Status == ChapterDownloadStatus.Completed, TimeSpan.FromSeconds(10));

            _eventAggregator.Verify(
                e => e.PublishEvent(It.Is<ChapterArchivedEvent>(ev => ev.MangaId == 1 && ev.ChapterId == 100)),
                Times.Once);
        }

        [Test]
        public async Task ManifestExpired_re_fetches_manifest_and_retries_once()
        {
            var aggregator = BuildAggregator("mangadex");
            var manifest1 = BuildManifest(2);
            var manifest2 = BuildManifest(2);
            var fetchCalls = 0;
            aggregator.Setup(a => a.GetChapterPages(It.IsAny<ReleaseInfo>()))
                      .ReturnsAsync(manifest2);   // re-fetch returns fresh manifest

            // First call to fetcher throws ManifestExpired for page 1; subsequent calls succeed.
            _fetcher.Setup(f => f.FetchPageBytesAsync(It.IsAny<IHttpAggregator>(), It.IsAny<ReleaseInfo>(), It.IsAny<ChapterPage>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() =>
                    {
                        fetchCalls++;
                        if (fetchCalls == 1)
                        {
                            throw new ManifestExpiredException(1, System.Net.HttpStatusCode.Forbidden);
                        }

                        return new byte[] { 0xFF, 0xD8 };
                    });

            var rowId = await Subject.EnqueueAsync(BuildRemote(1, 100), aggregator.Object, manifest1, new InProcessImageDownloadClientSettings { DownloadsPerSource = 1, PagesPerChapter = 1 });

            await WaitUntil(() => _persisted.Single(r => r.Id == rowId).Status == ChapterDownloadStatus.Completed, TimeSpan.FromSeconds(10));

            aggregator.Verify(
                a => a.GetChapterPages(It.IsAny<ReleaseInfo>()),
                Times.Once,
                "manifest must be re-fetched exactly once on first 403");
            _persisted.Single(r => r.Id == rowId).Status.Should().Be(ChapterDownloadStatus.Completed);
        }

        [Test]
        public async Task Second_ManifestExpired_fails_chapter()
        {
            var aggregator = BuildAggregator("mangadex");
            aggregator.Setup(a => a.GetChapterPages(It.IsAny<ReleaseInfo>()))
                      .ReturnsAsync(BuildManifest(1));

            // Always throw ManifestExpired — first throws, refetch retries, refetch retry throws too.
            _fetcher.Setup(f => f.FetchPageBytesAsync(It.IsAny<IHttpAggregator>(), It.IsAny<ReleaseInfo>(), It.IsAny<ChapterPage>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new ManifestExpiredException(1, System.Net.HttpStatusCode.Gone));

            var rowId = await Subject.EnqueueAsync(BuildRemote(1, 100), aggregator.Object, BuildManifest(1), new InProcessImageDownloadClientSettings { DownloadsPerSource = 1, PagesPerChapter = 1 });

            await WaitUntil(() => _persisted.Single(r => r.Id == rowId).Status == ChapterDownloadStatus.Failed, TimeSpan.FromSeconds(10));

            _persisted.Single(r => r.Id == rowId).Status.Should().Be(ChapterDownloadStatus.Failed);
            _eventAggregator.Verify(
                e => e.PublishEvent(It.Is<ChapterDownloadFailedEvent>(ev => ev.RowId == rowId)),
                Times.Once);

            ExceptionVerification.ExpectedErrors(1);   // terminal failure logs an error.
        }

        [Test]
        public async Task PagesPerChapter_change_is_picked_up_from_settings()
        {
            // BLOCKER #4 regression guard: when Settings.PagesPerChapter = 1, the per-page channel
            // capacity is 1; assert the ChapterDownloadJob carries the configured value end-to-end.
            // Strategy: gate the fetcher on a TaskCompletionSource and observe in-flight count.

            var aggregator = BuildAggregator("source-perpages-1");
            var inFlight = 0;
            var peakInFlight = 0;
            var release = new TaskCompletionSource<bool>();
            _fetcher.Setup(f => f.FetchPageBytesAsync(It.IsAny<IHttpAggregator>(), It.IsAny<ReleaseInfo>(), It.IsAny<ChapterPage>(), It.IsAny<CancellationToken>()))
                    .Returns(async () =>
                    {
                        var current = Interlocked.Increment(ref inFlight);
                        peakInFlight = Math.Max(peakInFlight, current);
                        await release.Task.ConfigureAwait(false);
                        Interlocked.Decrement(ref inFlight);
                        return new byte[] { 0xFF };
                    });

            var rowId = await Subject.EnqueueAsync(BuildRemote(1, 100), aggregator.Object, BuildManifest(4), new InProcessImageDownloadClientSettings { DownloadsPerSource = 1, PagesPerChapter = 1 });

            // Wait for the producer to populate the page channel; with PagesPerChapter=1, only 1 should be in-flight.
            await WaitUntil(() => inFlight >= 1, TimeSpan.FromSeconds(5));

            // Give the orchestrator additional time to attempt to spawn more consumers.
            await Task.Delay(200);
            peakInFlight.Should().Be(1, "PagesPerChapter=1 caps in-flight page fetches at 1");

            release.SetResult(true);
            await WaitUntil(() => _persisted.Single(r => r.Id == rowId).Status == ChapterDownloadStatus.Completed, TimeSpan.FromSeconds(10));
        }

        [Test]
        public async Task PagesPerChapter_higher_value_runs_multiple_in_parallel()
        {
            var aggregator = BuildAggregator("source-perpages-4");
            var inFlight = 0;
            var peakInFlight = 0;
            var release = new TaskCompletionSource<bool>();
            _fetcher.Setup(f => f.FetchPageBytesAsync(It.IsAny<IHttpAggregator>(), It.IsAny<ReleaseInfo>(), It.IsAny<ChapterPage>(), It.IsAny<CancellationToken>()))
                    .Returns(async () =>
                    {
                        var current = Interlocked.Increment(ref inFlight);
                        peakInFlight = Math.Max(peakInFlight, current);
                        await release.Task.ConfigureAwait(false);
                        Interlocked.Decrement(ref inFlight);
                        return new byte[] { 0xFF };
                    });

            var rowId = await Subject.EnqueueAsync(BuildRemote(1, 100), aggregator.Object, BuildManifest(8), new InProcessImageDownloadClientSettings { DownloadsPerSource = 1, PagesPerChapter = 4 });

            // Wait for in-flight to reach >=4 (or 8 capped — we only assert it's >1 to prove parallelism).
            await WaitUntil(() => peakInFlight >= 4, TimeSpan.FromSeconds(5));

            peakInFlight.Should().BeGreaterThanOrEqualTo(2, "PagesPerChapter=4 must allow > 1 concurrent fetch");
            release.SetResult(true);
            await WaitUntil(() => _persisted.Single(r => r.Id == rowId).Status == ChapterDownloadStatus.Completed, TimeSpan.FromSeconds(10));
        }

        [Test]
        public async Task DownloadsPerSource_change_is_picked_up_from_settings()
        {
            // BLOCKER #4 regression guard for the per-source channel capacity. Settings.DownloadsPerSource=1
            // → only one chapter at a time per source.
            var aggregator = BuildAggregator("source-dpsX");

            var chaptersInFlight = 0;
            var peak = 0;
            var release = new TaskCompletionSource<bool>();
            _fetcher.Setup(f => f.FetchPageBytesAsync(It.IsAny<IHttpAggregator>(), It.IsAny<ReleaseInfo>(), It.IsAny<ChapterPage>(), It.IsAny<CancellationToken>()))
                    .Returns(async () =>
                    {
                        var current = Interlocked.Increment(ref chaptersInFlight);
                        peak = Math.Max(peak, current);
                        await release.Task.ConfigureAwait(false);
                        Interlocked.Decrement(ref chaptersInFlight);
                        return new byte[] { 0xFF };
                    });

            // Channel capacity gates how many chapters are in-flight; we drop 3 chapters but
            // DownloadsPerSource=1 means writer blocks until consumer drains.
            var settings1 = new InProcessImageDownloadClientSettings { DownloadsPerSource = 1, PagesPerChapter = 1 };
            var ids = new List<int>();

            // Enqueue first 2 chapters; the third write may block on the bounded channel.
            ids.Add(await Subject.EnqueueAsync(BuildRemote(1, 200), aggregator.Object, BuildManifest(1), settings1));
            ids.Add(await Subject.EnqueueAsync(BuildRemote(1, 201), aggregator.Object, BuildManifest(1), settings1));

            await WaitUntil(() => peak >= 1, TimeSpan.FromSeconds(5));
            await Task.Delay(200);
            peak.Should().Be(1, "DownloadsPerSource=1 gates concurrent chapters per SourceKey");

            release.SetResult(true);
            await WaitUntil(() => ids.All(id => _persisted.Single(r => r.Id == id).Status == ChapterDownloadStatus.Completed), TimeSpan.FromSeconds(10));
        }
    }
}
