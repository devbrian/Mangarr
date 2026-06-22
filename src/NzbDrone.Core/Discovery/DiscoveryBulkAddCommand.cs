using System.Collections.Generic;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Discovery
{
    // Phase 42 Plan 42-03 (DISC-07) — fire-and-forget bulk-add orchestration command (D-07).
    //
    // Analog: ImportLists/ImportListSyncCommand.cs (exact shape). Carries the grid's
    // List<int> MangaBakaIds + the shared add-options the count-only modal (42-07) collects,
    // and is enqueued by DiscoveryController (42-04) via IManageCommandQueue.Push. The command
    // queue gives live progress (SendUpdatesToClient => true drives the ['/command'] queue UI
    // the FE already watches) + per-item failure capture for free.
    //
    // Diverges from ImportListSyncCommand: no scheduled-task cadence override — Discovery is
    // interactive-only (the command is never registered in TaskManager.defaultTasks, so the
    // base scheduling default is irrelevant).
    public class DiscoveryBulkAddCommand : Command
    {
        public List<int> MangaBakaIds { get; set; }
        public string RootFolderPath { get; set; }
        public MangaMonitor Monitor { get; set; }
        public int TranslationProfileId { get; set; }
        public int CustomFormatProfileId { get; set; }
        public List<int> Tags { get; set; }
        public bool SearchForMissingChapters { get; set; }

        public override bool SendUpdatesToClient => true;
    }
}
