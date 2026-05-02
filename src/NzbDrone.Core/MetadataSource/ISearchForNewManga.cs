using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Phase 2 split-contract per D-14 — search by user-typed title or by cross-source
    /// reverse-lookup ID. Mirrors Sonarr's <see cref="ISearchForNewSeries"/> split-contract
    /// precedent.
    /// </summary>
    public interface ISearchForNewManga
    {
        List<Manga.Manga> SearchForNewManga(string title);
        List<Manga.Manga> SearchForNewMangaByMangaDexId(string mangaDexId);
        List<Manga.Manga> SearchForNewMangaByAniListId(int aniListId);
        List<Manga.Manga> SearchForNewMangaByMalId(int malId);
    }
}
