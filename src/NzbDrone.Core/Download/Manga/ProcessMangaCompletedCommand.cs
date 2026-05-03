using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 RESEARCH Pattern 1 — see DIVERGENCE.md.
    // Role-match analog (loose): TV's CompletedDownloadService.Check polled via the
    // CheckForFinishedDownloadCommand cycle. Manga ships its own scheduled poll command so
    // ProcessMangaCompletedDownloads can implement BOTH IHandle<ChapterArchivedEvent>
    // (reactive, happy path) AND IExecute<ProcessMangaCompletedCommand> (resilience polling
    // ChapterDownloadState rows). Registered in TaskManager.defaultTasks at runtime per
    // sonarr-consistency-audit anti-pattern C (NOT seeded via 001 Insert.IntoTable).
    //
    // Phase 8 cleanup: collapse with TV's CheckForFinishedDownloadCommand when Tv/ deletes.
    public class ProcessMangaCompletedCommand : Command
    {
        public override bool SendUpdatesToClient => false;
    }
}
