using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.Commands
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit gap-01
    // (ImportApprovedEpisodes-vs-ImportApprovedChapters.md). Role-match analog:
    // src/NzbDrone.Core/MediaFiles/Commands/RescanSeriesCommand.cs.
    //
    // Pushed by ImportApprovedChapters.Import on DestinationAlreadyExistsException so
    // the orphan file on disk gets reconciled into the DB via a manga-side disk-scan.
    //
    // IExecute<RescanMangaCommand> handler: src/NzbDrone.Core/MediaFiles/MangaDiskScanService.cs
    // (shipped via Phase 9 close-out — see .planning/phases/09-service-sweep-backfill/09-CLOSE-OUT.md).
    // MangaDiskScanService walks Manga.Path filtered by MangaFileExtensions, builds LocalChapter
    // aggregates, runs the manga import-decision pipeline, and publishes MangaScannedEvent
    // (Pitfall 4 LAST). Mirrors RescanSeriesCommand surface — the manga handler IS a drop-in port.
    //
    // Comment-repair: Phase 11 sub-wave D Plan 11-07. The earlier comment incorrectly claimed
    // no IExecute handler existed (true pre-Phase-9 baseline; false post-Phase-9). Retained
    // here for future readers as a current-state pointer.
    //
    // Phase 14 cleanup: collapse with RescanSeriesCommand when Tv/ deletes.
    public class RescanMangaCommand : Command
    {
        public int? MangaId { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;

        public RescanMangaCommand()
        {
        }

        public RescanMangaCommand(int mangaId)
        {
            MangaId = mangaId;
        }
    }
}
