using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Download
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
    // TV-shape Download(RemoteEpisode, IIndexer) overload stripped; manga-shape
    // Download(RemoteChapter, IIndexer) is the canonical surface post-Tv/-DELETE.
    // Plan 15-06 already promoted the manga overload alongside the TV one; this plan
    // removes the TV one now that Plan 15-03 + 15-10 have eliminated all TV consumers.
    public interface IDownloadClient : IProvider
    {
        DownloadProtocol Protocol { get; }
        Task<string> Download(RemoteChapter remoteChapter, IIndexer indexer);
        IEnumerable<DownloadClientItem> GetItems();
        DownloadClientItem GetImportItem(DownloadClientItem item, DownloadClientItem previousImportAttempt);
        void RemoveItem(DownloadClientItem item, bool deleteData);
        DownloadClientInfo GetStatus();
        void MarkItemAsImported(DownloadClientItem downloadClientItem);
    }
}
