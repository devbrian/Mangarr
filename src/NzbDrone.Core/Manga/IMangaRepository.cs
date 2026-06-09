using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Manga
{
    // Repository contract for Manga aggregate. Mirrors Mangarr's ISeriesRepository
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
        Manga FindByTitle(string cleanTitle, int year);
        List<Manga> FindByTitleInexact(string cleanTitle);

        // GH #118 — exact-match lookup against the Manga.AlternativeTitles JSON column.
        // Strategy 2 of MangaParsingService.GetManga (Sonarr-canonical multi-strategy
        // mirroring ParsingService.GetSeries). Caller passes the
        // MangaTitleNormalizer-canonicalized form; this repo matches it against the
        // pre-normalized entries each metadata source wrote into AlternativeTitles.
        // Returns null on no match (mirrors FindByTitle single-row semantics).
        Manga FindByAlternativeTitle(string normalizedTitle);
        Manga FindByMangaDexId(Guid mangaDexId);
        Manga FindByMalId(int malId);
        Manga FindByAniListId(int aniListId);
        Manga FindByMangaBakaId(int mangaBakaId);
        Manga FindByPath(string path);
        Manga ReturnSingleMangaOrThrow(List<Manga> manga);
        List<Guid> AllMangaDexIds();
        List<int> AllMalIds();
        List<int> AllAniListIds();
        Dictionary<int, string> AllMangaPaths();
        Dictionary<int, List<int>> AllMangaTags();
    }
}
