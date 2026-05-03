using NzbDrone.Common.Messaging;
using NzbDrone.Core.MediaFiles.ChapterArchiving;

namespace NzbDrone.Core.Blocklisting.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-19 — see DIVERGENCE.md.
    // No TV analog: TV's BlocklistService inserts directly without an explicit "added" event;
    // its sole consumer (BlocklistSpecification) only reads via Blocklisted(...) at decision
    // time and does not need an event-ordering contract.
    //
    // Phase 6 D-19 — event-ordering contract for Plan 06-08 AutoRetryOrchestrator.
    // Published by MangaBlocklistService AFTER successful row Insert.
    // Synchronous fan-out via Sonarr's IEventAggregator guarantees: any subscriber
    // (e.g. AutoRetryOrchestrator) runs AFTER the row is committed; subsequent
    // BlocklistSpecification.Blocklisted(mangaId, release) queries see the row.
    //
    // Phase 8 cleanup: collapse with TV's Blocklist + Blocklist.Added pattern if/when one is
    // introduced. For now this lives only on the manga side.
    public class MangaBlocklistAddedEvent : IEvent
    {
        public MangaBlocklist Blocklist { get; }
        public ChapterDownloadFailedEvent SourceEvent { get; }

        public MangaBlocklistAddedEvent(MangaBlocklist blocklist, ChapterDownloadFailedEvent sourceEvent = null)
        {
            Blocklist = blocklist;
            SourceEvent = sourceEvent;
        }
    }
}
