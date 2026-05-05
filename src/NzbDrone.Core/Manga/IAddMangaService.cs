using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    /// <summary>
    /// META-02 add-manga orchestrator. Validates → resolves cross-source IDs (D-19..D-22) →
    /// persists via <see cref="IMangaService.AddManga(Manga)"/> → publishes <see cref="Events.MangaAddedEvent"/>
    /// (via the service) → schedules an initial <see cref="Commands.RefreshMangaCommand"/>.
    /// </summary>
    public interface IAddMangaService
    {
        Manga AddManga(Manga newManga);

        /// <summary>
        /// Bulk add overload mirroring <see cref="NzbDrone.Core.Tv.IAddSeriesService.AddSeries(System.Collections.Generic.List{NzbDrone.Core.Tv.Series}, bool)"/>.
        ///
        /// <para>Phase 8 audit gap-01 (AddSeriesService-vs-AddMangaService.md): used by future
        /// ImportLists pipeline (v2 IMP-01..03) + bulk-add UI flow. Per-item runs the same
        /// metadata-fetch + cross-source-resolution + validation pipeline as the single-add
        /// path; dedups against existing manga (by MangaDex/MAL/AniList ID) and against the
        /// in-progress batch; tolerates <see cref="FluentValidation.ValidationException"/> per
        /// item when <paramref name="ignoreErrors"/> is true. Persists via the bulk
        /// <see cref="IMangaService.AddManga(System.Collections.Generic.List{Manga})"/>
        /// overload — chapter-list synthesis is deferred to the per-item RefreshMangaCommand
        /// dispatched by MangaAddedHandler via MangaAddedEvent.</para>
        /// </summary>
        List<Manga> AddManga(List<Manga> newManga, bool ignoreErrors = false);
    }
}
