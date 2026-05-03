using NzbDrone.Core.Indexers.Http;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 — typed value object posted to the per-source <c>Channel&lt;ChapterDownloadJob&gt;</c>
    /// owned by <c>ChapterDownloadService</c> (plan 04-03). The DB row is the source of truth;
    /// this is just the in-memory carrier.
    ///
    /// BLOCKER #4 fix (revision 1): carries <c>PagesPerChapter</c> sourced from the active
    /// <c>InProcessImageDownloadClient.Definition.Settings</c> at enqueue time, so the
    /// orchestrator's per-page <c>Channel</c> capacity honors per-instance Settings.
    ///
    /// Aggregator type narrowed (plan 04-03): typed as <see cref="IHttpAggregator"/> now that
    /// plan 04-02 has landed the non-generic marker interface. The orchestrator uses
    /// <see cref="IHttpAggregator.GetChapterPages"/> for the D-03 reactive re-fetch.
    /// </summary>
    public sealed class ChapterDownloadJob
    {
        public ChapterDownloadState Row { get; }
        public IHttpAggregator Aggregator { get; }       // resolved via _indexerFactory.Get(release.IndexerId) in plan 04-03
        public int PagesPerChapter { get; }              // BLOCKER #4 — per-instance Settings honored

        public ChapterDownloadJob(ChapterDownloadState row, IHttpAggregator aggregator, int pagesPerChapter)
        {
            Row = row;
            Aggregator = aggregator;
            PagesPerChapter = pagesPerChapter;
        }
    }
}
