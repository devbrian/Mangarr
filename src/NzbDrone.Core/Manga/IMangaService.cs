using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    // Wave 0 stub — full surface lands in Plan 02-03.
    public interface IMangaService
    {
        Manga FindByTitle(string cleanTitle);
        Manga FindByMangaDexId(Guid mangaDexId);
        Manga FindByMalId(int malId);
        Manga FindByAniListId(int aniListId);
        Manga GetManga(int id);
        List<Manga> AllMangas();
        Manga AddManga(Manga newManga);
        Manga UpdateManga(Manga manga);
        void DeleteManga(int mangaId, bool deleteFiles);
    }
}
