using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Discovery
{
    // Phase 42 Plan 42-03 (DISC-07) — fire-and-forget bulk-add executor (D-07).
    //
    // Analog: ImportLists/ImportListSyncService (IExecute<ImportListSyncCommand> + the bulk
    // _addMangaService.AddManga(mangaToAdd, true) call at ImportListSyncService.cs:423). Builds
    // minimal Manga models from the grid's MangaBakaIds and hands them to the existing bulk
    // add path, which publishes MangaImportedEvent + fans out the per-item RefreshMangaCommand.
    //
    // A3 — NO extra throttle here: the refresh fan-out the bulk add triggers already self-paces
    // through the existing MangaBaka LookupRateLimit (0.5s == 120/min) on the shared bucket; the
    // controller (42-04) clamps the list to the grid's X (<=100). T-42-03-DOS.
    //
    // Title is left null — the refresh fan-out resolves the title from MangaBakaId (mirrors
    // ImportListSync's minimal-model pattern; A3).
    public class DiscoveryBulkAddCommandExecutor : IExecute<DiscoveryBulkAddCommand>
    {
        private readonly IAddMangaService _addMangaService;
        private readonly Logger _logger;

        public DiscoveryBulkAddCommandExecutor(IAddMangaService addMangaService, Logger logger)
        {
            _addMangaService = addMangaService;
            _logger = logger;
        }

        public void Execute(DiscoveryBulkAddCommand message)
        {
            var ids = message.MangaBakaIds ?? new List<int>();

            var mangaToAdd = ids.Select(id => new Manga.Manga
            {
                MangaBakaId = id,
                RootFolderPath = message.RootFolderPath,
                Monitored = true,
                TranslationProfileId = message.TranslationProfileId,
                CustomFormatProfileId = message.CustomFormatProfileId,
                Tags = new HashSet<int>(message.Tags ?? new List<int>()),
                AddOptions = new AddMangaOptions
                {
                    Monitor = message.Monitor,
                    SearchForMissingChapters = message.SearchForMissingChapters
                }
            }).ToList();

            _logger.ProgressInfo("Discovery bulk-add starting for {0} manga", mangaToAdd.Count);

            try
            {
                // ignoreErrors=true (the ImportListSync convention) so one bad MangaBakaId can't
                // abort the whole batch — per-item failures are isolated inside AddManga.
                // T-42-03-ISO.
                var added = _addMangaService.AddManga(mangaToAdd, true);

                _logger.ProgressInfo("Discovery bulk-add completed. Requested: {0}, Added: {1}", mangaToAdd.Count, added.Count);
            }
            catch (Exception ex)
            {
                // Failure isolation (T-42-03-ISO): never rethrow out of Execute — a batch-level
                // failure is logged + surfaced via the command queue, not propagated as an abort.
                _logger.Error(ex, "Discovery bulk-add failed for {0} manga", mangaToAdd.Count);
            }
        }
    }
}
