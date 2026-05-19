using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by RefreshMangaService.Execute after the iteration finishes.
    // Originally a verbatim parameterless mirror of Sonarr's SeriesRefreshCompleteEvent
    // (Tv/Events/SeriesRefreshCompleteEvent.cs); extended in gh199 to carry the optional
    // scope of mangas that were actually refreshed.
    //
    // Semantics:
    //   * MangaIds == null    → full-library refresh completed (refresh-all branch, OR
    //                           any consumer that constructs the event without an id
    //                           list, e.g. [CheckOn] reflection in RemovedMangaCheck).
    //                           Subscribers walk the whole library.
    //   * MangaIds.Count == 0 → explicit-IDs refresh where every requested id was
    //                           skipped (manga missing / scheduled cooldown / WR-08
    //                           no-source-id). Nothing changed; subscribers MUST NOT
    //                           fall back to a full-library walk — doing so silently
    //                           reintroduces the gh199 amplification whenever a batch
    //                           is fully skipped. Treat as a no-op. (See CodeRabbit
    //                           review on PR #215 for the catch that locked this in.)
    //   * MangaIds non-empty  → only those manga ids were refreshed. Subscribers that
    //                           operate per-manga (e.g. MangaAutoTaggingApplier) MUST
    //                           scope their iteration to this list rather than walking
    //                           the full library.
    //
    // This is a deliberate Mangarr divergence from the Sonarr-canonical parameterless
    // shape (see gh199 root-cause: applier-side amplification of GetTagChanges's
    // RootFolderPath side-effect across the full library on single-id refreshes).
    // Pairs with MangaRefreshStartingEvent (gap-01) emitted before the iteration starts.
    public class MangaRefreshCompleteEvent : IEvent
    {
        public IReadOnlyList<int> MangaIds { get; }

        public MangaRefreshCompleteEvent()
        {
            MangaIds = null;
        }

        public MangaRefreshCompleteEvent(IReadOnlyList<int> mangaIds)
        {
            MangaIds = mangaIds;
        }
    }
}
