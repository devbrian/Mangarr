using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 DOWNLOAD-01 / DOWNLOAD-06 — in-process IDownloadClient (no peer-fork precedent).
    ///
    /// Sonarr divergence: extends <see cref="DownloadClientBase{TSettings}"/> directly (NOT
    /// <c>UsenetClientBase</c> / <c>TorrentClientBase</c>) because manga is neither Usenet nor
    /// Torrent — Phase 1 D-04 introduced <see cref="DownloadProtocol.Http"/> as the third enum
    /// value. Closest behavioral analog is <c>UsenetBlackhole</c>'s "filesystem-as-truth
    /// <see cref="GetItems"/>" pattern; Phase 4 swaps the watch-folder for
    /// <see cref="IChapterDownloadStateRepository"/> so polling is O(rows) not O(directory walk).
    ///
    /// Auto-discovered by ThingiProvider reflection scan — no explicit DI registration needed.
    /// </summary>
    public class InProcessImageDownloadClient : DownloadClientBase<InProcessImageDownloadClientSettings>
    {
        public override string Name => "Mangarr In-Process Downloader";
        public override DownloadProtocol Protocol => DownloadProtocol.Http;   // Phase 1 D-04

        private readonly IIndexerFactory _indexerFactory;
        private readonly IChapterDownloadStateRepository _stateRepo;
        private readonly IChapterDownloadService _orchestrator;

        public InProcessImageDownloadClient(
            IIndexerFactory indexerFactory,
            IChapterDownloadStateRepository stateRepo,
            IChapterDownloadService orchestrator,
            IConfigService configService,
            IDiskProvider diskProvider,
            IRemotePathMappingService remotePathMappingService,
            Logger logger,
            ILocalizationService localizationService)
            : base(configService, diskProvider, remotePathMappingService, logger, localizationService)
        {
            _indexerFactory = indexerFactory;
            _stateRepo = stateRepo;
            _orchestrator = orchestrator;
        }

        // Phase 15 Wave (A) W-6 pre-land per
        // .planning/phases/15-domain-rename-rebrand/15-CONTRACTS-AUDIT.md §22.
        // Manga-shape overload sits alongside the TV-shape Download(RemoteEpisode, IIndexer).
        // Phase 15 Wave (C) deletes the TV overload when Tv/ deletes; the manga overload
        // becomes canonical. Both overloads coexist on Mangarr-v0 — purely additive.
        //
        // Routes through the existing TV-shape overload via a local conversion (NOT the
        // public RemoteChapter.ToRemoteEpisodeShim() static, which Wave (A) W-5 deletes).
        // The orchestrator's RemoteChapterJson serialization preserves the manga-relevant
        // fields (Manga.Id → Series.Id, Chapter.Id → Episode.Id) so Phase 4 staging-handoff
        // path is unaffected.
        public Task<string> Download(RemoteChapter remoteChapter, IIndexer indexer)
        {
            var manga = remoteChapter.Manga;
            var chapters = remoteChapter.Chapters ?? new List<NzbDrone.Core.Manga.Chapter>();

            var seriesShim = new NzbDrone.Core.Tv.Series { Id = manga?.Id ?? 0 };
            var episodeShims = chapters
                .Select(c => new NzbDrone.Core.Tv.Episode { Id = c.Id })
                .ToList();
            if (episodeShims.Count == 0)
            {
                episodeShims.Add(new NzbDrone.Core.Tv.Episode { Id = 0 });
            }

            var shim = new RemoteEpisode
            {
                Release = remoteChapter.Release,
                Series = seriesShim,
                Episodes = episodeShims,
                CustomFormats = remoteChapter.CustomFormats,
                CustomFormatScore = remoteChapter.CustomFormatScore
            };

            return Download(shim, indexer);
        }

        // Phase 4 thin shim — Phase 8 collapse renames RemoteEpisode → RemoteChapter.
        public override async Task<string> Download(RemoteEpisode remoteEpisode, IIndexer indexer)
        {
            // Pitfall 8 — DI-injected service must be CALLED, not just constructed.
            // The integration test InProcessImageDownloadClientFixture.Download_invokes_GetChapterPages
            // asserts this end-to-end (mirrors Phase 3 F-01 fix discipline).
            if (indexer is not IHttpAggregator aggregator)
            {
                throw new InvalidOperationException(
                    $"In-process downloader requires an HttpAggregatorBase indexer; got {indexer?.GetType().Name ?? "null"}.");
            }

            var manifest = await aggregator.GetChapterPages(remoteEpisode.Release).ConfigureAwait(false);

            // BLOCKER #4 fix: pass the per-instance Settings to the orchestrator so DownloadsPerSource
            // / PagesPerChapter come from THIS client's configured values (DOWNLOAD-02 honored).
            // DownloadClientBase exposes Settings via the protected Settings property (cast of Definition.Settings).
            var rowId = await _orchestrator.EnqueueAsync(remoteEpisode, aggregator, manifest, Settings).ConfigureAwait(false);
            return rowId.ToString("D");
        }

        // D-07 — DB projection on every poll; mirrors UsenetBlackhole/Aria2 query-on-demand shape.
        public override IEnumerable<DownloadClientItem> GetItems()
        {
            foreach (var row in _stateRepo.AllInFlight())
            {
                yield return new DownloadClientItem
                {
                    DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, hasPostImportCategory: false),
                    DownloadId = row.Id.ToString("D"),
                    Title = row.Title,
                    Status = MapStatus(row.Status),
                    TotalSize = EstimateTotalSize(row),
                    RemainingSize = EstimateRemaining(row),
                    OutputPath = row.Status == ChapterDownloadStatus.Completed && !string.IsNullOrEmpty(row.StagingPath)
                                    ? new OsPath(row.StagingPath)
                                    : new OsPath(null),
                    CanMoveFiles = true,
                    CanBeRemoved = row.Status == ChapterDownloadStatus.Completed
                                    || row.Status == ChapterDownloadStatus.Failed
                };
            }
        }

        public override DownloadClientItem GetImportItem(DownloadClientItem item, DownloadClientItem previousImportAttempt)
            => item;   // staging path already set on the item by GetItems

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            if (!int.TryParse(item.DownloadId, out var rowId))
            {
                return;
            }

            var row = _stateRepo.Get(rowId);
            if (row == null)
            {
                return;
            }

            _stateRepo.Delete(row.Id);

            if (deleteData)
            {
                DeleteItemData(item);   // base helper — DownloadClientBase.cs:110
                if (!string.IsNullOrEmpty(row.ScratchDir) && _diskProvider.FolderExists(row.ScratchDir))
                {
                    _diskProvider.DeleteFolder(row.ScratchDir, true);
                }
            }
        }

        // Phase 6 ImportApprovedChapters knows the staging root (<DataDir>/completed); we can
        // surface that here once the constant is centralized.
        public override DownloadClientInfo GetStatus()
            => new() { IsLocalhost = true, OutputRootFolders = new List<OsPath>() };

        protected override void Test(List<ValidationFailure> failures)
        {
            // Settings validate via IValidator; here we test the DownloadScratchPath is usable.
            failures.AddIfNotNull(TestFolder(_configService.DownloadScratchPath, "DownloadScratchPath"));
        }

        private static DownloadItemStatus MapStatus(ChapterDownloadStatus s) => s switch
        {
            ChapterDownloadStatus.Queued      => DownloadItemStatus.Queued,
            ChapterDownloadStatus.Downloading => DownloadItemStatus.Downloading,
            ChapterDownloadStatus.Completing  => DownloadItemStatus.Downloading,
            ChapterDownloadStatus.Completed   => DownloadItemStatus.Completed,
            ChapterDownloadStatus.Failed      => DownloadItemStatus.Failed,
            _                                 => DownloadItemStatus.Warning
        };

        private static long EstimateTotalSize(ChapterDownloadState row)
        {
            // D-12 — sum of Content-Length when known, else estimate 500KB/page × TotalPages.
            return row.EstimatedSizeBytes > 0
                ? row.EstimatedSizeBytes
                : (long)row.TotalPages * 500_000L;
        }

        private static long EstimateRemaining(ChapterDownloadState row)
        {
            var avgPerPage = row.TotalPages > 0 ? EstimateTotalSize(row) / row.TotalPages : 0L;
            var remaining = Math.Max(0, row.TotalPages - row.CompletedPages);
            return remaining * avgPerPage;
        }
    }
}
