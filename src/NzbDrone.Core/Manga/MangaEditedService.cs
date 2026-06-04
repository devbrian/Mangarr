using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Manga-side mirror of Sonarr's Tv/SeriesEditedService.cs. Sonarr's SeriesEditedService is
    // IHandle<SeriesEditedEvent> ONLY — it does not subscribe to SeriesBulkEditedEvent.
    //
    // Single-edit Handle(MangaEditedEvent) gates command-queue pushes on a diff against
    // message.OldManga (Phase 8 audit gap-09 substrate). Refresh fires on cross-source ID
    // triplet (MangaDexId/MalId/AniListId) change; Rescan fires on Manga.Path change
    // (Phase 32 CORR-02). No-op edits (e.g. Tags-only) push neither command.
    //
    // Bulk edits are intentionally NOT handled here (issue #264). In Sonarr the bulk-edit side
    // effects are owned by two OTHER subscribers, both already mirrored in Mangarr: SignalR
    // fan-out (MangaController.IHandle<MangaBulkEditedEvent>) and the explicit root-folder move
    // (BulkMoveMangaCommand, pushed by MangaEditorController.SaveAll when moveFiles=true). The
    // prior unconditional RefreshMangaCommand + RenameMangaCommand on the bulk path was a
    // Mangarr-invented divergence — NOT a Sonarr mirror, despite an earlier comment claiming so
    // — and was removed. Do not re-add a refresh/rename push here. Sonarr's OTHER bulk-edit side
    // effect — TrackedDownloadService's cache reconcile — is NOT owned here either: it now lives on
    // MangaDownloadMonitoringService.IHandle<MangaBulkEditedEvent> (+ the Added/Updated/Deleted
    // family — MangaUpdatedEvent, not MangaEditedEvent, so the move-rollback + refresh paths are
    // covered too), the manga owner of tracked-download state. That closed issue #278: its precondition
    // ("only if a downloader other than the in-process image client is added") went TRUE at Phase 39
    // (the external, asynchronously-tracked GatewayDownloadClient became the sole download client and
    // the #301 cross-poll registry began holding RemoteChapter.Manga across edits), so the
    // monitoring service now reconciles the affected rows' Manga snapshot in place and republishes
    // TrackedDownloadRefreshedEvent — immediate, matching Sonarr, instead of waiting for poll-rebuild.
    public class MangaEditedService : IHandle<MangaEditedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public MangaEditedService(IManageCommandQueue commandQueueManager,
                                  Logger logger)
        {
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Handle(MangaEditedEvent message)
        {
            // Single-edit path. Refresh gated on cross-source ID triplet (MangaDexId/MalId/AniListId) diff; Rescan pushed on Manga.Path diff (CORR-02).
            var pathChanged = !message.Manga.Path.PathEquals(message.OldManga.Path);
            var idsChanged = message.Manga.MangaDexId != message.OldManga.MangaDexId
                          || message.Manga.MalId != message.OldManga.MalId
                          || message.Manga.AniListId != message.OldManga.AniListId;

            if (pathChanged)
            {
                _logger.Debug("Manga {0} path changed ({1} -> {2}); queueing rescan.", message.Manga, message.OldManga.Path, message.Manga.Path);
                _commandQueueManager.Push(new RescanMangaCommand(message.Manga.Id));
            }

            if (idsChanged)
            {
                _logger.Debug("Manga {0} cross-source IDs changed; queueing refresh.", message.Manga);
                _commandQueueManager.Push(new RefreshMangaCommand(new List<int> { message.Manga.Id }, false));
            }
        }
    }
}
