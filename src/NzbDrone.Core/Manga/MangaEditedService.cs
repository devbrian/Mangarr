using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Manga-side mirror of Tv/SeriesEditedService.cs (Phase 8 audit cluster 04-edit-lifecycle,
    // audit gap `single` per audit/no-sibling/SeriesEditedService.md).
    //
    // PARITY DIVERGENCE — documented for SUMMARY/Phase 8 audit:
    //   - Sonarr's SeriesEditedService consumes SeriesEditedEvent (carries Series + OldSeries
    //     diff) and pushes RefreshSeriesCommand only when SeriesType changes.
    //   - Manga has NO standalone MangaEditedEvent; MangaUpdatedEvent (single-edit) carries
    //     only the new Manga (no OldManga snapshot), and MangaBulkEditedEvent (bulk-edit)
    //     carries List<Manga> only. We CANNOT compare old-vs-new fields.
    //   - Conservative initial port (matches the audit-report backfill_notes intent — refresh
    //     when a relink-style edit lands; can't detect-which-field-changed without OldManga):
    //       * MangaUpdatedEvent (single)        -> push RefreshMangaCommand for that manga
    //       * MangaBulkEditedEvent (bulk)       -> push RefreshMangaCommand + RenameMangaCommand
    //         for the full set; rename covers the case where a bulk-edit changed
    //         RootFolderPath / naming-relevant fields (TV's bulk path queues a rename pass too).
    //   - RescanMangaCommand does NOT yet exist (peer Tv/MediaFiles/Commands/RescanSeriesCommand
    //     has no manga sibling at this point in Phase 8). The path-change rescan branch is
    //     therefore deferred — see TODO below. RenameMangaCommand IS available
    //     (MediaFiles/Commands/RenameMangaCommand.cs) and is wired on the bulk path.
    //
    // TODO (future Phase 8 cluster, post-RescanMangaCommand backfill):
    //   - When a RescanMangaCommand is added on the manga side, push it here on the path-
    //     changed branch (TV pushes Rescan after edits that move the series folder).
    //   - When MangaUpdatedEvent / a future MangaEditedEvent grows an OldManga snapshot,
    //     gate the refresh push behind cross-source ID change detection (MangaDexId, MalId,
    //     AniListId — manual relink trigger per audit-report backfill_notes).
    public class MangaEditedService : IHandle<MangaUpdatedEvent>, IHandle<MangaBulkEditedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MangaEditedService(IManageCommandQueue commandQueueManager,
                                  IConfigService configService,
                                  Logger logger)
        {
            _commandQueueManager = commandQueueManager;
            _configService = configService;
            _logger = logger;
        }

        public void Handle(MangaUpdatedEvent message)
        {
            // Single-edit path: refresh metadata for the edited manga. Conservative port —
            // without an OldManga snapshot we cannot gate this on field-level change detection,
            // so we always queue a refresh on update. See class-level DIVERGENCE note.
            _logger.Debug("Manga {0} updated; queueing refresh.", message.Manga);
            _commandQueueManager.Push(new RefreshMangaCommand(new List<int> { message.Manga.Id }, false));
        }

        public void Handle(MangaBulkEditedEvent message)
        {
            // Bulk-edit path: queue a refresh for every edited manga and a rename pass over
            // the full set. TV's equivalent (SeriesBulkEditedEvent handler in Sonarr) treats
            // bulk edits as potentially folder-affecting (root folder moves, monitor toggles
            // that may rename) and queues a rename. Without OldManga snapshots we mirror that
            // conservative behavior here.
            if (message.Manga == null || message.Manga.Count == 0)
            {
                return;
            }

            var mangaIds = message.Manga.Select(m => m.Id).ToList();
            _logger.Debug("Bulk manga edit ({0} entities); queueing refresh + rename.", mangaIds.Count);
            _commandQueueManager.Push(new RefreshMangaCommand(mangaIds, false));
            _commandQueueManager.Push(new RenameMangaCommand { MangaIds = mangaIds });
        }
    }
}
