using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-01 / LOOP-05) — see DIVERGENCE.md.
    // Provenance: v5-develop:src/NzbDrone.Core/Download/TrackedDownloads/RefreshMonitoredDownloadsCommand.cs
    //   (the poll-trigger command DownloadMonitoringService implements IExecute<> against).
    // Role-match analog (exact shape): the retired in-process ProcessMangaCompletedCommand
    //   (payload-less Command, SendUpdatesToClient => false) — deleted in Phase 39 RETIRE-01.
    //
    // Registered in TaskManager.defaultTasks at a 1-minute cadence at RUNTIME per
    // sonarr-consistency-audit anti-pattern C (NEVER seeded via a migration Insert.IntoTable).
    // The MangaDownloadMonitoringService IExecute<RefreshMonitoredMangaDownloadsCommand> handler
    // polls every DownloadHandlingEnabled() client, tracks each in-flight item, runs the
    // Completed/Failed Checks, and publishes TrackedDownloadRefreshedEvent as the LAST step
    // (the dead-queue fix — wakes the starved MangaQueueService → SignalR cascade).
    //
    // Phase 38 cleanup: collapse with the gateway-path monitor when the external client lands.
    public class RefreshMonitoredMangaDownloadsCommand : Command
    {
        public override bool SendUpdatesToClient => false;
    }
}
