using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
            // quick-260618-eqz — matches against BOTH the metadata-sourced
            // AlternativeTitles JSON column AND the user-owned UserAlternativeTitles
            // JSON column. Entries in BOTH columns are pre-normalized at write-time
            // (metadata sources via MapManga; user titles via MangaResourceMapper.ToModel
            // — see Task 3), so an exact substring match wrapped in JSON-element quotes
            // (e.g. `"shingeki no kyojin"`) uniquely identifies a stored entry without
            // false positives — the surrounding quotes prevent matching a value that is a
            // substring of another normalized entry. The columns are expected to hold
            // pre-normalized entries (mirrors the AlternativeTitles write-time-normalized
            // note).
            //
            // Inherits the BL-02 null-input guard from FindByTitle for the
            // parser short-path that may pass a malformed/empty title.
            //
            // SQLite uses `instr`; PostgreSQL uses `strpos`. Mirrors the
            // FindByTitleInexact dual-dialect branch above. The broadened WHERE
            // matches when the pattern appears in EITHER column. PostgreSQL uses
            // coalesce(strpos(...),0) because UserAlternativeTitles is nullable and
            // strpos(NULL, …) yields NULL; SQLite instr(NULL, …) already yields NULL
            // which compares `> 0` as false, so the SQLite branch needs no coalesce —
            // but the OR is kept explicit on both dialects.
            //
            // Ambiguity safety (gh118 code-review followup): alt-title entries
            // are NOT uniqueness-guaranteed across the library — two manga can
            // legitimately share a romanized synonym (e.g. a doujinshi and its
            // parent series both list the romanized parent title in their
            // attributes.altTitles), or two users could add the same custom
            // alias to different manga. Returning FirstOrDefault would be
            // nondeterministic — the first-row ordering depends on insert
            // order. Return null on ambiguous (>1 candidate) matches (across
            // EITHER list) so the caller (MangaParsingService.GetManga) falls
            // through to FindByTitleInexact, matching the Sonarr-canonical posture
            // and the existing FindByTitleInexact contract.
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return null;
            }

            // Build the search token by serializing through the SAME System.Text.Json
            // path the StringListConverter uses to WRITE the column (default encoder,
            // no Encoder override), so the escaping matches byte-for-byte. The
            // StringListConverter serializes List<string> as a JSON array
            // (e.g. ["abc","def"]), so each entry is a double-quoted JSON string;
            // matching the quoted JSON-string form uniquely identifies an entry
            // within the array. Crucially, the default encoder escapes non-ASCII
            // (e.g. CJK "鬼滅の刃" -> "鬼滅の刃") in BOTH the stored
            // column AND this token, so user-added CJK aliases (common for manga)
            // resolve too — a raw quote-wrap matched only ASCII aliases (Codex
            // PR #377 P2). For ASCII titles JsonSerializer.Serialize("abc") yields
            // "abc" — identical to the previous manual wrap, so no behavior change.
            var pattern = JsonSerializer.Serialize(normalizedTitle);

            var builder = Builder().Where($"(instr(\"Manga\".\"AlternativeTitles\", @pattern) > 0 OR instr(\"Manga\".\"UserAlternativeTitles\", @pattern) > 0)", new { pattern });

            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                builder = Builder().Where($"(coalesce(strpos(\"Manga\".\"UserAlternativeTitles\", @pattern),0) > 0 OR coalesce(strpos(\"Manga\".\"AlternativeTitles\", @pattern),0) > 0)", new { pattern });
            }

            var candidates = Query(builder).Take(2).ToList();
            return candidates.Count == 1 ? candidates[0] : null;
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

        public Manga FindByMangaBakaId(int mangaBakaId)
        {
            return Query(m => m.MangaBakaId == mangaBakaId).SingleOrDefault();
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
