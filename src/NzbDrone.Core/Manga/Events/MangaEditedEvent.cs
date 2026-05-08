using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by MangaService.UpdateManga on the USER-EDIT path
    // (publishUpdatedEvent: true). Mirrors Mangarr's SeriesEditedEvent
    // (Tv/Events/SeriesEditedEvent.cs) verbatim shape: carries the new manga, the
    // pre-update snapshot, and a chaptersChanged flag (TV: episodesChanged).
    //
    // SEMANTIC SPLIT (Phase 8 audit gap-09):
    //   * MangaEditedEvent  — user-edit path (MangaService.UpdateManga). Carries
    //                          new + old so handlers can diff (path change → move,
    //                          monitor flip → refresh, etc.).
    //   * MangaUpdatedEvent — refresh path (RefreshMangaService trailing post-sync
    //                          pulse). Carries only the new snapshot — refresh
    //                          handlers re-read whatever they need.
    public class MangaEditedEvent : IEvent
    {
        public Manga Manga { get; private set; }
        public Manga OldManga { get; private set; }
        public bool ChaptersChanged { get; private set; }

        public MangaEditedEvent(Manga manga, Manga oldManga, bool chaptersChanged = false)
        {
            Manga = manga;
            OldManga = oldManga;
            ChaptersChanged = chaptersChanged;
        }
    }
}
