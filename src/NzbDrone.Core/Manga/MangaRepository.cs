using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Dapper repository for Manga. Mirrors Mangarr's SeriesRepository
    // (Tv/SeriesRepository.cs:25-138) shape verbatim. Inherits Phase 1 D-15 Polly
    // retry coverage automatically via BasicRepository<T>.
    public class MangaRepository : BasicRepository<Manga>, IMangaRepository
    {
        public MangaRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public bool MangaPathExists(string path)
        {
            return Query(m => m.Path == path).Any();
        }

        public Manga FindByTitle(string cleanTitle)
        {
            // BL-02 fix: short-circuit null/empty input to prevent NRE on
            // ToLowerInvariant. Public IBasicRepository<Manga> consumers and future
            // indexer plumbing may pass a null title from a malformed parser short-path.
            if (string.IsNullOrWhiteSpace(cleanTitle))
            {
                return null;
            }

            cleanTitle = cleanTitle.ToLowerInvariant();

            return Query(m => m.CleanTitle == cleanTitle).SingleOrDefault();
        }

        public Manga FindByTitle(string cleanTitle, int year)
        {
            // Phase 8 audit gap-01 (SeriesRepository-vs-MangaRepository.md): year-disambiguating
            // overload mirrors Tv/SeriesRepository.FindByTitle(string, int) at line 47-54.
            // Manga axis is PublicationYear (nullable int) per Manga.cs:79; TV axis is the
            // non-nullable Series.Year. Inherits the BL-02 null-input guard from the
            // single-arg overload above for consistency on parser short-paths.
            if (string.IsNullOrWhiteSpace(cleanTitle))
            {
                return null;
            }

            cleanTitle = cleanTitle.ToLowerInvariant();

            return Query(m => m.CleanTitle == cleanTitle && m.PublicationYear == year).SingleOrDefault();
        }

        public Manga FindByAlternativeTitle(string normalizedTitle)
        {
            // GH #118 — Strategy 2 of MangaParsingService.GetManga multi-strategy
            // resolution (Sonarr-canonical mirror of ParsingService.GetSeries).
            //
            // Matches against the AlternativeTitles JSON column written by the
            // metadata sources. Entries are pre-normalized at write-time
            // (MangaTitleNormalizer.Normalize), so an exact substring match
            // wrapped in JSON-element quotes (e.g. `"shingeki no kyojin"`)
            // uniquely identifies a stored entry without false positives —
            // the surrounding quotes prevent matching a value that is a
            // substring of another normalized entry.
            //
            // Inherits the BL-02 null-input guard from FindByTitle for the
            // parser short-path that may pass a malformed/empty title.
            //
            // SQLite uses `instr`; PostgreSQL uses `strpos`. Mirrors the
            // FindByTitleInexact dual-dialect branch above.
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return null;
            }

            // Wrap in JSON-element quotes — the StringListConverter serializes
            // List<string> as a JSON array (e.g. ["abc","def"]), so each
            // entry is enclosed in double quotes. Matching '"abc"' (with quotes)
            // uniquely identifies the entry "abc" within the JSON array.
            var pattern = "\"" + normalizedTitle + "\"";

            var builder = Builder().Where($"instr(\"Manga\".\"AlternativeTitles\", @pattern) > 0", new { pattern });

            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                builder = Builder().Where($"(strpos(\"Manga\".\"AlternativeTitles\", @pattern) > 0)", new { pattern });
            }

            return Query(builder).FirstOrDefault();
        }

        public List<Manga> FindByTitleInexact(string cleanTitle)
        {
            // Phase 8 audit gap-02 (SeriesRepository-vs-MangaRepository.md): mirrors
            // Tv/SeriesRepository.FindByTitleInexact at line 56-66 verbatim. Used by the
            // parser's substring fallback path (`INSTR` on SQLite, `STRPOS` on PostgreSQL)
            // to match any manga whose CleanTitle is a substring of the release title.
            // Inherits the BL-02 null-input guard pattern from FindByTitle for parser
            // short-paths that may pass a malformed/empty title.
            if (string.IsNullOrWhiteSpace(cleanTitle))
            {
                return new List<Manga>();
            }

            var builder = Builder().Where($"instr(@cleanTitle, \"Manga\".\"CleanTitle\")", new { cleanTitle = cleanTitle });

            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                builder = Builder().Where($"(strpos(@cleanTitle, \"Manga\".\"CleanTitle\") > 0)", new { cleanTitle = cleanTitle });
            }

            return Query(builder).ToList();
        }

        public Manga FindByMangaDexId(Guid mangaDexId)
        {
            return Query(m => m.MangaDexId == mangaDexId).SingleOrDefault();
        }

        public Manga FindByMalId(int malId)
        {
            return Query(m => m.MalId == malId).SingleOrDefault();
        }

        public Manga FindByAniListId(int aniListId)
        {
            return Query(m => m.AniListId == aniListId).SingleOrDefault();
        }

        public Manga FindByPath(string path)
        {
            return Query(m => m.Path == path).FirstOrDefault();
        }

        public Manga ReturnSingleMangaOrThrow(List<Manga> manga)
        {
            // Phase 8 audit gap-04 (SeriesRepository-vs-MangaRepository.md): mirrors
            // Tv/SeriesRepository.ReturnSingleSeriesOrThrow at line 124-137 verbatim.
            // Returns null on empty, the single match on count==1, otherwise throws
            // MultipleMangaFoundException carrying the matched-list so callers can
            // disambiguate (e.g., year-overload re-query) instead of swallowing the
            // InvalidOperationException .SingleOrDefault() would raise on multi-match.
            if (manga.Count == 0)
            {
                return null;
            }

            if (manga.Count == 1)
            {
                return manga.First();
            }

            throw new MultipleMangaFoundException(manga, "Expected one manga, but found {0}. Matching manga: {1}", manga.Count, string.Join(", ", manga));
        }

        public List<Guid> AllMangaDexIds()
        {
            return All()
                .Where(m => m.MangaDexId.HasValue)
                .Select(m => m.MangaDexId.Value)
                .ToList();
        }

        public List<int> AllMalIds()
        {
            return All()
                .Where(m => m.MalId.HasValue)
                .Select(m => m.MalId.Value)
                .ToList();
        }

        public List<int> AllAniListIds()
        {
            return All()
                .Where(m => m.AniListId.HasValue)
                .Select(m => m.AniListId.Value)
                .ToList();
        }

        public Dictionary<int, string> AllMangaPaths()
        {
            return All().ToDictionary(m => m.Id, m => m.Path);
        }

        public Dictionary<int, List<int>> AllMangaTags()
        {
            using (var conn = _database.OpenConnection())
            {
                var strSql = "SELECT \"Id\" AS Key, \"Tags\" AS Value FROM \"Manga\" WHERE \"Tags\" IS NOT NULL";
                return conn.Query<KeyValuePair<int, List<int>>>(strSql).ToDictionary(x => x.Key, x => x.Value);
            }
        }
    }
}
