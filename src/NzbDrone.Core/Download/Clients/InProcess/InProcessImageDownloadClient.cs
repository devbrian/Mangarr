using System;
using System.Collections.Generic;
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

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape Download(RemoteEpisode)
        // and the local-shim conversion stripped; manga-shape Download(RemoteChapter) is canonical.
        // Pitfall 8 — DI-injected service must be CALLED, not just constructed.
        public override async Task<string> Download(RemoteChapter remoteChapter, IIndexer indexer)
        {
            if (indexer is not IHttpAggregator aggregator)
            {
                throw new InvalidOperationException(
                    $"In-process downloader requires an HttpAggregatorBase indexer; got {indexer?.GetType().Name ?? "null"}.");
            }

            var manifest = await aggregator.GetChapterPages(remoteChapter.Release).ConfigureAwait(false);

            // BLOCKER #4 fix: pass the per-instance Settings to the orchestrator so DownloadsPerSource
            // / PagesPerChapter come from THIS client's configured values (DOWNLOAD-02 honored).
            var rowId = await _orchestrator.EnqueueAsync(remoteChapter, aggregator, manifest, Settings).ConfigureAwait(false);
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
