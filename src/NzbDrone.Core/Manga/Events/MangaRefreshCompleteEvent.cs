using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by RefreshMangaService.Execute after the iteration finishes.
    // Mirrors Mangarr's SeriesRefreshCompleteEvent (Tv/Events/SeriesRefreshCompleteEvent.cs)
    // verbatim shape — parameterless pulse so UI / SignalR subscribers can clear the
    // "refresh in progress" indicator. Pairs with MangaRefreshStartingEvent (gap-01).
    public class MangaRefreshCompleteEvent : IEvent
    {
    }
}
