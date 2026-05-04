namespace NzbDrone.Core.Manga
{
    // Phase 8 backfill — audit gap-03 (RefreshSeriesService-vs-RefreshMangaService.md) +
    // no-sibling/ShouldRefreshSeries.md. Manga-side parity for TV
    // ICheckIfSeriesShouldBeRefreshed (Tv/ShouldRefreshSeries.cs:8-11).
    //
    // Consumed by RefreshMangaService.Execute on the scheduled (empty MangaIds) branch
    // to gate the per-manga refresh against the LastInfoSync/Status heuristic. Closes
    // the rate-limit-budget exposure documented in PROJECT.md D-22 — without this gate
    // every scheduled tick hammered MangaDex/AniList/MAL for every manga unconditionally.
    public interface IShouldRefreshManga
    {
        bool ShouldRefresh(Manga manga);
    }
}
