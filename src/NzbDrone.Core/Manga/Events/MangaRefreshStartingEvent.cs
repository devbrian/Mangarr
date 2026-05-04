using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by RefreshMangaService.Execute as the very first action,
    // before any iteration. Mirrors Sonarr's SeriesRefreshStartingEvent
    // (Tv/Events/SeriesRefreshStartingEvent.cs) verbatim shape — carries a single
    // ManualTrigger flag so UI / SignalR subscribers can distinguish manual vs.
    // scheduled refreshes when surfacing the "refresh starting" indicator. Pairs
    // with MangaRefreshCompleteEvent (gap-02) emitted after the iteration finishes.
    public class MangaRefreshStartingEvent : IEvent
    {
        public bool ManualTrigger { get; set; }

        public MangaRefreshStartingEvent(bool manualTrigger)
        {
            ManualTrigger = manualTrigger;
        }
    }
}
