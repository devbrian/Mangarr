using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Download.Manga.Builders
{
    // Sonarr divergence: NEW Phase 36 shared test infrastructure (Wave 0 gap) — see DIVERGENCE.md.
    // No production analog: this is a test-only fluent builder reused by the Plan 02/03/04 fixtures.
    //
    // Mirrors the realistic projection that InProcessImageDownloadClient.GetItems() emits
    // (src/NzbDrone.Core/Download/Clients/InProcess/InProcessImageDownloadClient.cs:74-94 +
    // EstimateTotalSize/EstimateRemaining :168-181): a non-zero TotalSize via the page-estimate
    // path, an OutputPath = staging path on completion, and CanBeRemoved on Completed/Failed.
    // Keep minimal, deterministic, and free of ChapterDownloadState coupling (gateway-path-clean).
    public class DownloadClientItemBuilder
    {
        // 4 pages × 500KB/page — mirrors the InProcess EstimateTotalSize default (TotalPages × 500_000).
        private const long DefaultTotalSize = 4 * 500_000L;

        private readonly DownloadClientItem _item;
        private bool _outputPathExplicitlySet;

        public DownloadClientItemBuilder()
        {
            _item = new DownloadClientItem
            {
                DownloadId = "1",
                Title = "Test Manga - Chapter 001",
                Status = DownloadItemStatus.Downloading,
                TotalSize = DefaultTotalSize,
                RemainingSize = DefaultTotalSize,
                OutputPath = new OsPath(null),
                CanMoveFiles = true,
                CanBeRemoved = false,
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = DownloadProtocol.Http,
                    Type = "InProcessImageDownloadClient",
                    Id = 1,
                    Name = "InProcess"
                }
            };
        }

        public DownloadClientItemBuilder WithDownloadId(string downloadId)
        {
            _item.DownloadId = downloadId;
            return this;
        }

        public DownloadClientItemBuilder WithTitle(string title)
        {
            _item.Title = title;
            return this;
        }

        public DownloadClientItemBuilder Downloading(long totalSize, long remainingSize)
        {
            _item.Status = DownloadItemStatus.Downloading;
            _item.TotalSize = totalSize;
            _item.RemainingSize = remainingSize;
            _item.CanBeRemoved = false;
            return this;
        }

        public DownloadClientItemBuilder WithOutputPath(string path)
        {
            _item.OutputPath = new OsPath(path);
            _outputPathExplicitlySet = true;
            return this;
        }

        public DownloadClientItemBuilder Completed()
        {
            _item.Status = DownloadItemStatus.Completed;
            _item.RemainingSize = 0;
            _item.CanBeRemoved = true;

            // GetItems() sets OutputPath = StagingPath on completion. Derive the default from the
            // builder's Title (so Title/OutputPath stay consistent — e.g. WithTitle("Pack") →
            // C:\staging\Pack) unless the caller already set one via WithOutputPath().
            if (!_outputPathExplicitlySet)
            {
                _item.OutputPath = new OsPath($@"C:\staging\{_item.Title}");
            }

            return this;
        }

        public DownloadClientItemBuilder Failed()
        {
            _item.Status = DownloadItemStatus.Failed;
            _item.CanBeRemoved = true;
            return this;
        }

        public DownloadClientItem Build()
        {
            return _item;
        }
    }
}
