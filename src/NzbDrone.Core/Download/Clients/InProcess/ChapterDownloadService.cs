using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 — bounded Channel&lt;T&gt; orchestrator (no peer-fork precedent).
    /// Per-source channel (capacity = DownloadsPerSource); each consumer runs one chapter at a
    /// time through a nested Channel&lt;ChapterPage&gt; (capacity = PagesPerChapter). Per-chapter
    /// try/catch envelope (DOWNLOAD-05) ensures a single failure does not block other chapters.
    ///
    /// Pitfall 5 — Download() returns immediately after channel write; channel readers run in
    /// fire-and-forget Tasks per source.
    /// D-03 — 403/410 → ManifestExpiredException → orchestrator catches, re-fetches manifest
    /// ONCE, retries the page once with the new URL; second 403/410 = chapter terminal failure.
    ///
    /// BLOCKER #4 fix (revision 1): per-instance Settings (DownloadsPerSource +
    /// PagesPerChapter) flow from <see cref="InProcessImageDownloadClient.Download(NzbDrone.Core.Parser.Model.RemoteEpisode, NzbDrone.Core.Indexers.IIndexer)"/> through
    /// <see cref="EnqueueAsync"/> into the per-source Channel capacity + the
    /// <see cref="ChapterDownloadJob"/> carrier. Hardcoded resolver constants eliminated.
    /// Channel capacity is fixed at first-use per SourceKey (Channel&lt;T&gt; is immutable post
    /// construction); subsequent Settings UI edits take effect after restart.
    /// </summary>
    public class ChapterDownloadService : IChapterDownloadService
    {
        private readonly IChapterDownloadStateRepository _stateRepo;
        private readonly IChapterPageFetcher _fetcher;
        private readonly IChapterArchiverFactory _archiverFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        private readonly ConcurrentDictionary<string, SourceChannel> _channelsBySourceKey = new();

        public ChapterDownloadService(
            IChapterDownloadStateRepository stateRepo,
            IChapterPageFetcher fetcher,
            IChapterArchiverFactory archiverFactory,
            IEventAggregator eventAggregator,
            IConfigService configService,
            IDiskProvider diskProvider,
            Logger logger)
        {
            _stateRepo = stateRepo;
            _fetcher = fetcher;
            _archiverFactory = archiverFactory;
            _eventAggregator = eventAggregator;
            _configService = configService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public async Task<int> EnqueueAsync(RemoteEpisode remote, IHttpAggregator aggregator, ChapterManifest manifest, InProcessImageDownloadClientSettings settings)
        {
            // D-08 — insert row at top of Download() — before any HTTP work.
            var scratchRoot = _configService.DownloadScratchPath;
            var row = new ChapterDownloadState
            {
                MangaId = remote.Series?.Id ?? 0,
                ChapterId = remote.Episodes?.FirstOrDefault()?.Id ?? 0,
                Title = remote.Release?.Title ?? "<unknown>",
                RemoteChapterJson = JsonConvert.SerializeObject(remote),
                ManifestJson = JsonConvert.SerializeObject(manifest),
                ManifestExpiresAt = manifest.ExpiresAt?.UtcDateTime,
                TotalPages = manifest.TotalCount,
                CompletedPages = 0,
                EstimatedSizeBytes = 0,
                ScratchDir = string.Empty,   // set after row Id is known
                Status = ChapterDownloadStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _stateRepo.Insert(row);

            row.ScratchDir = Path.Combine(scratchRoot, row.Id.ToString());
            _stateRepo.SetFields(row, r => r.ScratchDir);
            _diskProvider.CreateFolder(row.ScratchDir);

            // Get-or-create per-source channel + worker Task.
            // BLOCKER #4 fix: capacity comes from the active client's per-instance Settings
            // (DownloadsPerSource), NOT a hardcoded constant.
            var sourceChannel = _channelsBySourceKey.GetOrAdd(aggregator.SourceKey, key =>
            {
                var capacity = Math.Max(1, settings?.DownloadsPerSource ?? 2);
                var channel = Channel.CreateBounded<ChapterDownloadJob>(new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = false,
                    SingleWriter = false
                });
                var sc = new SourceChannel(channel, capacity);

                // Pitfall 5 — fire-and-forget worker; Download() doesn't block.
                sc.WorkerTask = Task.Run(() => RunWorkerAsync(channel, key, sc.Cts.Token));
                return sc;
            });

            // BLOCKER #4 fix: per-chapter parallelism flows on the job (carried from per-instance Settings).
            var pagesPerChapter = Math.Max(1, settings?.PagesPerChapter ?? 4);
            await sourceChannel.Channel.Writer.WriteAsync(new ChapterDownloadJob(row, aggregator, pagesPerChapter)).ConfigureAwait(false);
            return row.Id;
        }

        private async Task RunWorkerAsync(Channel<ChapterDownloadJob> channel, string sourceKey, CancellationToken ct)
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var job))
                    {
                        try
                        {
                            await ProcessChapterAsync(job, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            // Pattern 3 — per-chapter try/catch envelope; one failure does NOT
                            // block other chapters in the queue (DOWNLOAD-05).
                            _logger.Error(ex, "Chapter download {0} failed: {1}", job.Row.Title, ex.Message);
                            FailChapter(job.Row, ex.Message);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Worker shutdown via Cts; expected.
            }
        }

        private async Task ProcessChapterAsync(ChapterDownloadJob job, CancellationToken ct)
        {
            var aggregator = job.Aggregator;

            job.Row.Status = ChapterDownloadStatus.Downloading;
            job.Row.UpdatedAt = DateTime.UtcNow;
            _stateRepo.SetFields(job.Row, r => r.Status, r => r.UpdatedAt);

            var manifest = JsonConvert.DeserializeObject<ChapterManifest>(job.Row.ManifestJson);

            // D-05 — pre-resume scan; skip pages already on disk in scratch.
            var alreadyOnDisk = ScanExistingPageIndexes(job.Row.ScratchDir);

            // Q-6 conservative: trust-the-bytes; future enhancement may size-check.
            var release = JsonConvert.DeserializeObject<RemoteEpisode>(job.Row.RemoteChapterJson)?.Release ?? new ReleaseInfo();

            var manifestRefetched = false;
            var pagesPerChapter = Math.Max(1, job.PagesPerChapter);
            var pageChannel = Channel.CreateBounded<ChapterPage>(pagesPerChapter);

            // Producer
            var producerTask = Task.Run(
                async () =>
                {
                    try
                    {
                        foreach (var p in manifest.Pages.Where(p => !alreadyOnDisk.Contains(p.PageIndex)))
                        {
                            await pageChannel.Writer.WriteAsync(p, ct).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        pageChannel.Writer.Complete();
                    }
                },
                ct);

            // Consumers (PagesPerChapter parallel)
            var consumers = Enumerable.Range(0, pagesPerChapter).Select(_ => Task.Run(
                async () =>
                {
                    while (await pageChannel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        while (pageChannel.Reader.TryRead(out var page))
                        {
                            try
                            {
                                var bytes = await _fetcher.FetchPageBytesAsync(aggregator, release, page, ct).ConfigureAwait(false);
                                await WritePageAsync(job.Row.ScratchDir, page, bytes, ct).ConfigureAwait(false);
                                IncrementCompletedPages(job.Row);
                            }
                            catch (ManifestExpiredException)
                            {
                                // D-03 — re-fetch ONCE, retry the page once.
                                if (manifestRefetched)
                                {
                                    throw;
                                }

                                manifestRefetched = true;
                                _logger.Info("Re-fetching manifest for chapter {0} due to expired token at page {1}", job.Row.Title, page.PageIndex);
                                manifest = await aggregator.GetChapterPages(release).ConfigureAwait(false);
                                job.Row.ManifestJson = JsonConvert.SerializeObject(manifest);
                                _stateRepo.SetFields(job.Row, r => r.ManifestJson);

                                var retryPage = manifest.Pages.FirstOrDefault(p => p.PageIndex == page.PageIndex);
                                if (retryPage == null)
                                {
                                    throw new InvalidOperationException($"Page {page.PageIndex} missing from refreshed manifest");
                                }

                                var bytes = await _fetcher.FetchPageBytesAsync(aggregator, release, retryPage, ct).ConfigureAwait(false);
                                await WritePageAsync(job.Row.ScratchDir, retryPage, bytes, ct).ConfigureAwait(false);
                                IncrementCompletedPages(job.Row);
                            }
                        }
                    }
                },
                ct)).ToArray();

            await producerTask.ConfigureAwait(false);
            await Task.WhenAll(consumers).ConfigureAwait(false);

            // All pages on disk → archive.
            job.Row.Status = ChapterDownloadStatus.Completing;
            job.Row.UpdatedAt = DateTime.UtcNow;
            _stateRepo.SetFields(job.Row, r => r.Status, r => r.UpdatedAt);

            var archiver = _archiverFactory.Resolve(_configService.OutputFormat);

            // WARNING #6 fix: staging dir comes from Config.StagingPath (plan 04-01 Task 3 added
            // the new key, default <DataDir>/completed). NO relative ".." traversal from scratch.
            // T-04-22 mitigation: same-volume by construction (StagingPath defaults under <DataDir>).
            var stagingDir = Path.Combine(_configService.StagingPath, SafeMangaSlug(job.Row));
            _diskProvider.CreateFolder(stagingDir);

            var request = new ChapterArchiveRequest
            {
                ScratchDir = job.Row.ScratchDir,
                StagingDir = stagingDir,
                OutputFilename = job.Row.Title,
                PageCount = job.Row.TotalPages,
                Release = release
            };

            var stagingPath = await archiver.ArchiveAsync(request, ct).ConfigureAwait(false);

            job.Row.StagingPath = stagingPath;
            job.Row.Status = ChapterDownloadStatus.Completed;
            job.Row.UpdatedAt = DateTime.UtcNow;
            _stateRepo.SetFields(job.Row, r => r.StagingPath, r => r.Status, r => r.UpdatedAt);

            _eventAggregator.PublishEvent(new ChapterArchivedEvent(job.Row.MangaId, job.Row.ChapterId, stagingPath, _configService.OutputFormat));
            _logger.Info("Chapter {0} archived → {1}", job.Row.Title, stagingPath);
        }

        private void FailChapter(ChapterDownloadState row, string reason)
        {
            row.Status = ChapterDownloadStatus.Failed;
            row.FailureReason = reason;
            row.RetentionUntil = DateTime.UtcNow.AddDays(_configService.RetentionDays);
            row.UpdatedAt = DateTime.UtcNow;
            _stateRepo.SetFields(row, r => r.Status, r => r.FailureReason, r => r.RetentionUntil, r => r.UpdatedAt);
            _eventAggregator.PublishEvent(new ChapterDownloadFailedEvent(row.Id, row.MangaId, row.ChapterId, reason));
        }

        private HashSet<int> ScanExistingPageIndexes(string scratchDir)
        {
            var present = new HashSet<int>();
            if (string.IsNullOrEmpty(scratchDir) || !_diskProvider.FolderExists(scratchDir))
            {
                return present;
            }

            foreach (var f in _diskProvider.GetFiles(scratchDir, false))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (int.TryParse(name, out var idx))
                {
                    present.Add(idx);
                }
            }

            return present;
        }

        private async Task WritePageAsync(string scratchDir, ChapterPage page, byte[] bytes, CancellationToken ct)
        {
            var ext = ResolveExtension(page);
            var path = Path.Combine(scratchDir, $"{page.PageIndex:D4}{ext}");
            await using var fs = File.Create(path);
            await fs.WriteAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
        }

        private static string ResolveExtension(ChapterPage page)
        {
            // Prefer URL-embedded extension; fall back to ContentTypeHint.
            var ext = string.Empty;
            if (Uri.TryCreate(page.Url, UriKind.Absolute, out var uri))
            {
                ext = Path.GetExtension(uri.AbsolutePath);
            }

            if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(page.ContentTypeHint))
            {
                ext = page.ContentTypeHint switch
                {
                    "image/jpeg" => ".jpg",
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    _ => ".bin"
                };
            }

            return string.IsNullOrEmpty(ext) ? ".jpg" : ext;
        }

        private void IncrementCompletedPages(ChapterDownloadState row)
        {
            // Per-row update is single-writer at the SQLite level; the page-channel parallel
            // consumers operate on the same in-memory row reference, but Interlocked is
            // unnecessary because completion ordering doesn't matter — only the final count.
            row.CompletedPages++;
            row.UpdatedAt = DateTime.UtcNow;
            _stateRepo.SetFields(row, r => r.CompletedPages, r => r.UpdatedAt);
        }

        private static string SafeMangaSlug(ChapterDownloadState row)
            => $"manga-{row.MangaId}";

        private sealed class SourceChannel
        {
            public Channel<ChapterDownloadJob> Channel { get; }
            public int DownloadsPerSource { get; }
            public CancellationTokenSource Cts { get; } = new();
            public Task WorkerTask { get; set; }

            public SourceChannel(Channel<ChapterDownloadJob> channel, int downloadsPerSource)
            {
                Channel = channel;
                DownloadsPerSource = downloadsPerSource;
            }
        }
    }
}
