using System.Collections.Generic;
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
    public class MangaAddedHandler : IHandle<MangaAddedEvent>
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
    }
}
