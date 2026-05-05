using System.Collections.Generic;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.DecisionEngine.Manga;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-08 (sub-wave A 09-02 audit gap-01 close-out, Plan 09-12)
    // — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Indexers/RssSyncCompleteEvent.cs.
    //
    // Payload simplification vs TV: manga MangaRssSyncService has no grab path
    // (Phase 6 split — MangaRssSyncService.cs lines 65-68 documents the rationale; the grab
    // path lives in MangaReleaseController + AutoRetryOrchestrator). The event payload
    // therefore carries a flat List<MangaDownloadDecision> rather than TV's tri-split
    // ProcessedDecisions { Grabbed, Pending, Rejected }. Consumers (Plan 09-10
    // MangaPendingReleaseService.Handle) filter via the .Rejected / .Approved derived
    // properties on MangaDownloadDecision when pruning the pending-release queue.
    //
    // BL-01 GUARD: payload type is manga-shape (RemoteChapter via MangaDownloadDecision),
    // NOT TV-shape (RemoteEpisode via DownloadDecision). Cross-shape cast hazard
    // mechanically impossible — Plan 09-10 IHandle subscriber consumes RemoteChapter directly.
    //
    // Phase 14 cleanup: collapse with RssSyncCompleteEvent when Tv/ deletes (drop the
    // Manga-prefix; the manga payload shape becomes canonical because TV grab path
    // is also dropped).
    public class MangaRssSyncCompleteEvent : IEvent
    {
        public List<MangaDownloadDecision> ProcessedDecisions { get; private set; }

        public MangaRssSyncCompleteEvent(List<MangaDownloadDecision> processedDecisions)
        {
            ProcessedDecisions = processedDecisions;
        }
    }
}
