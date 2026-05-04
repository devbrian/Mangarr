using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // Sonarr divergence: NEW manga event sibling per Phase 7 D-07 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Tv/Events/EpisodeUpdatedEvent.cs.
    //
    // API-01: published when ChapterController flips chapter.Monitored. Drives the
    // SignalR `chapter` resource broadcast (auto-derived from bare [V5ApiController]
    // on ChapterController) so the React Manga Details page reflects within ~1s.
    //
    // Phase 8 cleanup: collapse with EpisodeUpdatedEvent when Tv/ deletes.
    public class ChapterUpdatedEvent : IEvent
    {
        public Chapter Chapter { get; private set; }

        public ChapterUpdatedEvent(Chapter chapter)
        {
            Chapter = chapter;
        }
    }
}
