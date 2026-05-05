using NzbDrone.Common.Messaging;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.MediaCover
{
    // Sonarr divergence: NEW manga sibling per Phase 9 D-09 (sub-wave A 09-04 audit gap-03 close-out, Plan 09-13)
    // — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaCover/MediaCoversUpdatedEvent.cs.
    //
    // Carries the resolved Manga aggregate + a `bool Updated` flag tracked across the
    // MangaMediaCoverService.HandleAsync(MangaUpdatedEvent) cover-download foreach. The
    // flag is true iff at least one cover was newly downloaded (the AlreadyExists short-circuit
    // path leaves it false). MangaController.Handle(MangaCoversUpdatedEvent) consumes the
    // event and broadcasts a SignalR Updated for the manga.Id ONLY when Updated == true,
    // mirroring SeriesController.cs:415-421. UI thumbnail re-render fires after the disk
    // writes complete (Pitfall 4 ordering — disk FIRST, event LAST).
    //
    // BL-01 GUARD: payload type is manga-shape (`Manga` from NzbDrone.Core.Manga), NOT
    // TV-shape (`Series` from NzbDrone.Core.Tv). MangaController.IHandle<MangaCoversUpdatedEvent>
    // is the only subscriber; SeriesController.IHandle<MediaCoversUpdatedEvent> subscribes
    // to its own TV event independently. Cross-domain dispatch is type-system-impossible.
    //
    // Phase 14 cleanup: collapse with MediaCoversUpdatedEvent when Tv/ deletes (drop the
    // Manga-prefix; the manga payload shape becomes canonical because Series is renamed to Manga).
    public class MangaCoversUpdatedEvent : IEvent
    {
        public Manga Manga { get; set; }
        public bool Updated { get; set; }

        public MangaCoversUpdatedEvent(Manga manga, bool updated)
        {
            Manga = manga;
            Updated = updated;
        }
    }
}
