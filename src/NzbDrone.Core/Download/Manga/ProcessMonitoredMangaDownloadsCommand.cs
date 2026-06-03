using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-04) — see DIVERGENCE.md.
    // Provenance: v5-develop:src/NzbDrone.Core/Download/ProcessMonitoredDownloadsCommand.cs
    //   (the command DownloadProcessingService implements IExecute<> against; pushed at the tail of
    //   DownloadMonitoringService.Refresh()).
    // Role-match analog (exact shape): the retired in-process ProcessMangaCompletedCommand
    //   (payload-less Command, SendUpdatesToClient => false) — deleted in Phase 39 RETIRE-01.
    //
    // QUEUED-ONLY — NO TaskManager.defaultTasks row. The MangaDownloadMonitoringService pushes this
    // command onto the command queue at the tail of every Refresh() (Q-poll resolution); it is never
    // on its own scheduled cadence. The MangaDownloadProcessingService implements
    // IExecute<ProcessMonitoredMangaDownloadsCommand> to import pending / process failed / evict
    // removable downloads. TaskManagerDefaultTasksFixture explicitly asserts this command is NOT
    // registered in defaultTasks.
    //
    // Phase 38 cleanup: collapse with the gateway-path processing service when the external client lands.
    public class ProcessMonitoredMangaDownloadsCommand : Command
    {
        public override bool SendUpdatesToClient => false;
    }
}
