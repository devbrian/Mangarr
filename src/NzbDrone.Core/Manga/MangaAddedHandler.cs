using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Phase 8 audit gap-01 (SeriesAddedEvent-vs-MangaAddedEvent): mirrors
    // Tv/SeriesAddedHandler.cs. Decouples auto-refresh from AddMangaService
    // by subscribing to the published MangaAddedEvent and pushing
    // RefreshMangaCommand. Restores Sonarr's IHandle pattern so additional
    // consumers (e.g. v2 ImportLists) can piggyback on the post-add hook
    // without editing AddMangaService.
    //
    // Phase 8 audit gap-10 (SeriesService-vs-MangaService.md): bulk-import
    // path mirrors Tv/SeriesAddedHandler.Handle(SeriesImportedEvent) at line
    // 25-28 — batches RefreshMangaCommands into a single PushMany rather than
    // firing N separate Push calls (rate-limit budget pressure on ImportList
    // ingestion). Single-add path Handle(MangaAddedEvent) is unchanged.
    public class MangaAddedHandler : IHandle<MangaAddedEvent>,
                                     IHandle<MangaImportedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;

        public MangaAddedHandler(IManageCommandQueue commandQueueManager)
        {
            _commandQueueManager = commandQueueManager;
        }

        public void Handle(MangaAddedEvent message)
        {
            _commandQueueManager.Push(new RefreshMangaCommand(new List<int> { message.Manga.Id }, isNewManga: true));
        }

        public void Handle(MangaImportedEvent message)
        {
            _commandQueueManager.PushMany(message.MangaIds.Select(m => new RefreshMangaCommand(new List<int> { m }, isNewManga: true)).ToList());
        }
    }
}
