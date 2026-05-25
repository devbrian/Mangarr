using System;
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
    // Single-edit Handle(MangaEditedEvent) gates command-queue pushes on a diff against
    // message.OldManga (Phase 8 audit gap-09 substrate). Refresh fires on cross-source ID
    // triplet (MangaDexId/MalId/AniListId) change; Rescan fires on Manga.Path change
    // (Phase 32 CORR-02). No-op edits (e.g., Tags-only) push neither command.
    //
    // Bulk-edit Handle(MangaBulkEditedEvent) preserves the conservative always-refresh +
    // rename behavior because MangaBulkEditedEvent carries List<Manga> only with no
    // OldManga snapshots — the per-entity diff cannot be applied (Phase 32 D-07; bulk-path
    // tightening tracked for v1.3+ if user signal emerges).
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
            // Single-edit path. Refresh gated on cross-source ID triplet (MangaDexId/MalId/AniListId) diff; Rescan pushed on Manga.Path diff (CORR-02).
            var pathChanged = !string.Equals(message.Manga.Path, message.OldManga.Path, StringComparison.Ordinal);
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
