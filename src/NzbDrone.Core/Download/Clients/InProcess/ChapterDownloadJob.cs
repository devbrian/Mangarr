using NzbDrone.Core.Indexers;

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
    /// Aggregator type: typed as <see cref="IIndexer"/> for plan-04-01 compile independence.
    /// Plan 04-02 introduces a non-generic <c>IHttpAggregator</c> marker interface that
    /// <c>HttpAggregatorBase&lt;TSettings&gt;</c> implements; plan 04-03 may narrow the type
    /// once 04-02 lands.
    /// </summary>
    public sealed class ChapterDownloadJob
    {
        public ChapterDownloadState Row { get; }
        public IIndexer Aggregator { get; }       // resolved via _indexerFactory.Get(release.IndexerId) in plan 04-03
        public int PagesPerChapter { get; }       // BLOCKER #4 — per-instance Settings honored

        public ChapterDownloadJob(ChapterDownloadState row, IIndexer aggregator, int pagesPerChapter)
        {
            Row = row;
            Aggregator = aggregator;
            PagesPerChapter = pagesPerChapter;
        }
    }
}
