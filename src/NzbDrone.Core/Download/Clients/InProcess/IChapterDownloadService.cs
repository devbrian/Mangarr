using System.Threading.Tasks;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 plan 04-03 — orchestrator contract for the in-process image download client.
    /// Inserts a <see cref="ChapterDownloadState"/> row + writes a <see cref="ChapterDownloadJob"/>
    /// to the per-source bounded channel. Returns the new row Id (used as
    /// <c>DownloadClientItem.DownloadId</c>).
    ///
    /// BLOCKER #4 fix (revision 1): the <c>settings</c> argument is the per-instance
    /// <see cref="InProcessImageDownloadClientSettings"/> POCO of the calling
    /// <see cref="InProcessImageDownloadClient"/> — flowed through so the orchestrator honors
    /// per-instance <c>DownloadsPerSource</c> + <c>PagesPerChapter</c> values rather than
    /// hardcoded constants.
    /// </summary>
    public interface IChapterDownloadService
    {
        Task<int> EnqueueAsync(RemoteEpisode remote, IHttpAggregator aggregator, ChapterManifest manifest, InProcessImageDownloadClientSettings settings);
    }
}
