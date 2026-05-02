using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Manga
{
    // Repository contract for Manga aggregate. Mirrors Sonarr's ISeriesRepository
    // (Tv/SeriesRepository.cs:9-23) shape verbatim, with manga-domain divergence on
    // the Find methods per 02-CONTEXT specifics: singular cross-source IDs
    // (FindByMangaDexId/MalId/AniListId) because manga has 1:1 source mapping (vs.
    // anime's 1:N pluralized FindByMalIds/FindByAniListIds).
    //
    // Inherits IBasicRepository<Manga> (Insert/Update/Delete/Get/All/Find/Upsert/
    // SetFields/InsertMany/UpdateMany/DeleteMany/Purge/HasItems/GetPaged) plus
    // automatic Polly retry (Phase 1 D-15) via BasicRepository<T>.
    public interface IMangaRepository : IBasicRepository<Manga>
    {
        bool MangaPathExists(string path);
        Manga FindByTitle(string cleanTitle);
        Manga FindByMangaDexId(Guid mangaDexId);
        Manga FindByMalId(int malId);
        Manga FindByAniListId(int aniListId);
        Manga FindByPath(string path);
        List<Guid> AllMangaDexIds();
        List<int> AllMalIds();
        List<int> AllAniListIds();
        Dictionary<int, string> AllMangaPaths();
    }
}
