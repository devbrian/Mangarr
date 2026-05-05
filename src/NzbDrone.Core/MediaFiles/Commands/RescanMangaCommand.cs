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
    // NOTE: No IExecute<RescanMangaCommand> handler exists yet — no manga-side rescan
    // service has been ported from TV's RescanSeriesService. Until the handler ships
    // (follow-up plan), the command is queued and silently dropped by the
    // UnknownCommandExecutor fallback. Mirrors RescanSeriesCommand surface so the
    // future handler is a drop-in port.
    //
    // Phase 8 cleanup: collapse with RescanSeriesCommand when Tv/ deletes.
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
