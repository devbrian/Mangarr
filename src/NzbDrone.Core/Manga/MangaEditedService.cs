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
    // Phase 8 audit gap-09 (MangaEditedEvent semantic split — landed in MangaService.UpdateManga):
    //   - Mangarr's SeriesEditedService consumes SeriesEditedEvent (carries Series + OldSeries
    //     diff) and pushes RefreshSeriesCommand only when SeriesType changes.
    //   - MangaEditedEvent NOW carries `Manga` + `OldManga` + `ChaptersChanged` (mirrors
    //     SeriesEditedEvent) so this handler can diff the old-vs-new snapshot to gate the
    //     refresh push (path change → rescan, cross-source ID flip → relink-style refresh,
    //     etc.). Conservative port still queues a refresh unconditionally — TV's
    //     SeriesEditedService only refreshes on SeriesType change, but the manga equivalent
    //     ("MangaType change") is rare AND existing fixtures assert refresh-on-update; we
    //     keep the always-queue behavior pending an explicit Phase 8 follow-up audit.
    //   - MangaBulkEditedEvent (bulk-edit) still carries List<Manga> only — bulk path is
    //     unchanged by gap-09.
    //   - RescanMangaCommand does NOT yet exist (peer Tv/MediaFiles/Commands/RescanSeriesCommand
    //     has no manga sibling at this point in Phase 8). The path-change rescan branch is
    //     therefore deferred — see TODO below. RenameMangaCommand IS available
    //     (MediaFiles/Commands/RenameMangaCommand.cs) and is wired on the bulk path.
    //
    // TODO (future Phase 8 cluster, post-RescanMangaCommand backfill):
    //   - When a RescanMangaCommand is added on the manga side, push it here on the path-
    //     changed branch (compare message.Manga.Path vs message.OldManga.Path; TV pushes
    //     Rescan after edits that move the series folder).
    //   - Gate the refresh push behind cross-source ID change detection
    //     (message.OldManga.MangaDexId/MalId/AniListId vs message.Manga's — manual relink
    //     trigger per audit-report backfill_notes).
    public class MangaEditedService : IHandle<MangaEditedEvent>, IHandle<MangaBulkEditedEvent>
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

        public void Handle(MangaEditedEvent message)
        {
            // Single-edit path: refresh metadata for the edited manga. Conservative port —
            // we now have OldManga but keep unconditional refresh for now; gating on
            // field-level diffs is a future iteration (see class-level TODO).
            _logger.Debug("Manga {0} edited; queueing refresh.", message.Manga);
            _commandQueueManager.Push(new RefreshMangaCommand(new List<int> { message.Manga.Id }, false));
        }

        public void Handle(MangaBulkEditedEvent message)
        {
            // Bulk-edit path: queue a refresh for every edited manga and a rename pass over
            // the full set. TV's equivalent (SeriesBulkEditedEvent handler in Mangarr) treats
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
