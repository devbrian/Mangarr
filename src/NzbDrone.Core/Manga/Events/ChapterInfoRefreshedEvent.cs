using System.Collections.Generic;
using System.Collections.ObjectModel;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event carrying the delta of a chapter-info refresh: the Manga whose chapter
    // list was synced plus the Added / Updated / Removed chapter collections produced by
    // the refresh pipeline. Mirrors Sonarr's Tv/Events/EpisodeInfoRefreshedEvent.cs shape
    // verbatim (precedent: PROJECT.md "Preserve Sonarr's shape wherever it works").
    //
    // Distinct from sibling ChapterListUpdatedEvent — that event is a SignalR-style
    // "manga changed, re-read from repo" pulse and intentionally omits the delta.
    // This event is for consumers that need the inline delta (e.g. the future
    // ChapterRefreshedService auto-search-on-discovery trigger; Phase 8 backfill plan
    // 01-03, audit gap no-sibling/EpisodeInfoRefreshedEvent.md).
    //
    // Publish call site is intentionally NOT wired here — that responsibility belongs
    // to a separate plan (RefreshSeriesService-vs-RefreshMangaService.md gap-10).
    public class ChapterInfoRefreshedEvent : IEvent
    {
        public Manga Manga { get; set; }
        public ReadOnlyCollection<Chapter> Added { get; private set; }
        public ReadOnlyCollection<Chapter> Updated { get; private set; }
        public ReadOnlyCollection<Chapter> Removed { get; private set; }

        public ChapterInfoRefreshedEvent(Manga manga, IList<Chapter> added, IList<Chapter> updated, IList<Chapter> removed)
        {
            Manga = manga;
            Added = new ReadOnlyCollection<Chapter>(added);
            Updated = new ReadOnlyCollection<Chapter>(updated);
            Removed = new ReadOnlyCollection<Chapter>(removed);
        }
    }
}
