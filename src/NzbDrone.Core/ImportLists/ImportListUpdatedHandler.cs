using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListUpdatedHandler.cs
    // with generic IImportList → IMangaImportList swap. When a user edits an existing
    // ImportList definition (V5 controller PUT) the ProviderFactory raises
    // ProviderUpdatedEvent<IMangaImportList>; this handler queues a single-list
    // ImportListSyncCommand so the new settings take effect on the next tick.
    public class ImportListUpdatedHandler : IHandle<ProviderUpdatedEvent<IMangaImportList>>
    {
        private readonly IManageCommandQueue _commandQueueManager;

        public ImportListUpdatedHandler(IManageCommandQueue commandQueueManager)
        {
            _commandQueueManager = commandQueueManager;
        }

        public void Handle(ProviderUpdatedEvent<IMangaImportList> message)
        {
            _commandQueueManager.Push(new ImportListSyncCommand(message.Definition.Id));
        }
    }
}
