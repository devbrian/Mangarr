using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Queue.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-20 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Queue/QueueUpdatedEvent.cs.
    //
    // Phase 6 D-20 — emitted by MangaQueueService on every queue mutation
    // (refresh from TrackedDownloadRefreshedEvent). Plan 06-09 V5 controller's
    // SignalR hub subscribes to fan out queue diffs to React.
    //
    // Phase 8 cleanup: collapse with QueueUpdatedEvent when Tv/ deletes.
    public class MangaQueueUpdatedEvent : IEvent
    {
    }

    // Sonarr divergence: NEW manga sibling per Phase 6 D-20 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Queue/PendingReleasesUpdatedEvent.cs.
    //
    // Reserved for the Plan 06-08 auto-retry orchestrator hand-off path: when a
    // pending release is held / promoted, the queue projection refreshes and any
    // SignalR consumer that distinguishes pending from in-flight gets a separate
    // signal. Phase 8 cleanup: collapse with PendingReleasesUpdatedEvent.
    public class MangaPendingReleasesUpdatedEvent : IEvent
    {
    }
}
