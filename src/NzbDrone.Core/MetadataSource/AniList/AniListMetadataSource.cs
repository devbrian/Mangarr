using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.MetadataSource.AniList
{
    /// <summary>
    /// AniList GraphQL metadata source per D-25 — NEW file. The anime-side
    /// <c>src/NzbDrone.Core/ImportLists/AniList/AniListAPI.cs</c> stays UNTOUCHED.
    ///
    /// Behavior:
    ///   * <see cref="DefaultIsPrimary"/> = false — secondary fallback per D-16. Users may promote
    ///     via <c>MetadataSourceFactory.SetPrimary</c>.
    ///   * <see cref="DefaultSourceKey"/> = "anilist" — every outbound request carries
    ///     <c>RateLimitKey = "anilist"</c> (30 req/min budget per Phase 1 STACK research).
    ///   * <see cref="GetMangaInfo(string)"/> returns Manga + EMPTY chapter list — AniList exposes
    ///     no per-chapter feed in v1 GraphQL. The synthesis fallback (D-17) populates rows from
    ///     <c>media.chapters</c> total-count when MangaDex is not cross-resolved.
    ///   * Primary author extracted from <c>staff.edges</c> with <c>role == "Story"</c> per D-21.
    ///   * 429 handler logs warning per RESEARCH §Pitfall 6; <c>IIndexerStatusService</c> integration
    ///     is deferred to Phase 3 indexer side (Phase 2 metadata source is fire-once-per-add).
    /// </summary>
    public class AniListMetadataSource : HttpMetadataSourceBase<AniListMetadataSourceSettings>
    {
        public AniListMetadataSource(IHttpClient httpClient, Logger logger)
            : base(httpClient, logger)
        {
        }

        public override string Name => "AniList";

        public override string DefaultSourceKey => "anilist";

        public override bool DefaultIsPrimary => false;     // D-16 — secondary fallback

        public override Tuple<Manga.Manga, List<Chapter>> GetMangaInfo(string sourceId)
        {
            if (!int.TryParse(sourceId, out var anilistId))
            {
                throw new MangaNotFoundException(sourceId, $"Not a valid AniList integer ID: {sourceId}");
            }

            var media = QueryMediaById(anilistId)
                ?? throw new MangaNotFoundException(sourceId);

            // AniList exposes NO per-chapter feed in v1; return EMPTY chapter list. The
            // synthesis fallback (D-17) populates rows from media.Chapters total-count when
            // MangaDex is not cross-resolved.
            return Tuple.Create(MapManga(media), new List<Chapter>());
        }

        public override List<Manga.Manga> SearchForNewManga(string title)
        {
            var page = QueryMediaSearch(MangaTitleNormalizer.Normalize(title));
            return page?.Media?.Select(MapManga).ToList() ?? new List<Manga.Manga>();
        }

        public override List<Manga.Manga> SearchForNewMangaByMangaDexId(string mangaDexId)
        {
            // AniList does NOT support cross-source ID search by MangaDex GUID; resolver hits
            // MangaDex first then forwards the resolved AniList ID via SearchForNewMangaByAniListId.
            return new List<Manga.Manga>();
        }

        public override List<Manga.Manga> SearchForNewMangaByAniListId(int aniListId)
        {
            try
            {
                return new List<Manga.Manga> { GetMangaInfo(aniListId.ToString()).Item1 };
            }
            catch (MangaNotFoundException)
            {
                return new List<Manga.Manga>();
            }
        }

        public override List<Manga.Manga> SearchForNewMangaByMalId(int malId)
        {
            // AniList Media query supports lookup by idMal — issue a Media-by-idMal GraphQL.
            var page = QueryMediaByIdMal(malId);
            return page?.Media?.Select(MapManga).ToList() ?? new List<Manga.Manga>();
        }

        public override ValidationResult Test()
        {
            // WR-17 fix: use the cheapest GraphQL probe possible — fetch a single
            // media id by a known-stable AniList ID (1, "Cowboy Bebop") which is the
            // canonical AniList connectivity-check pattern. Pre-fix the Test issued a
            // full Page(perPage=10) media search which ate from the 30 req/min budget
            // every time the user clicked Test in the Settings UI.
            try
            {
                QueryMediaById(1);
                return new ValidationResult();
            }
            catch (Exception ex)
            {
                return new ValidationResult(new[] { new ValidationFailure("BaseUrl", ex.Message) });
            }
        }

        // ---- HTTP helpers ----

        private AniListMedia QueryMediaById(int id)
        {
            var body = AniListMangaApi.BuildBody(AniListMangaApi.MediaByIdQuery, new { id });
            var resp = PostGraphQl<MediaResponseShape>(body);
            return resp?.Data?.Media;
        }

        private AniListPage QueryMediaSearch(string search)
        {
            var body = AniListMangaApi.BuildBody(AniListMangaApi.MediaSearchQuery, new { search, page = 1 });
            var resp = PostGraphQl<PageResponseShape>(body);
            return resp?.Data?.Page;
        }

        private AniListPage QueryMediaByIdMal(int malId)
        {
            var body = AniListMangaApi.BuildBody(AniListMangaApi.MediaByIdMalQuery, new { idMal = malId });
            var resp = PostGraphQl<MediaResponseShape>(body);
            return resp?.Data?.Media != null
                ? new AniListPage { Media = new List<AniListMedia> { resp.Data.Media } }
                : null;
        }

        private AniListGraphQlResponse<T> PostGraphQl<T>(string body)
        {
            var req = BuildRequest(AniListMangaApi.GraphQlEndpoint);
            req.Method = HttpMethod.Post;
            req.Headers["Content-Type"] = "application/json";
            req.SetContent(body);

            try
            {
                var resp = _httpClient.Post<AniListGraphQlResponse<T>>(req);
                return resp.Resource;
            }
            catch (HttpException ex) when (ex.Response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Per RESEARCH §Pitfall 6: log warning. IIndexerStatusService integration deferred
                // to Phase 3 indexer side per RESEARCH (Phase 2 metadata source is fire-once-per-add).
                _logger.Warn("AniList returned 429 Too Many Requests; honor Retry-After + back off");
                throw;
            }
        }

        // ---- Mapping ----

        private static Manga.Manga MapManga(AniListMedia m)
        {
            var manga = new Manga.Manga
            {
                AniListId = m.Id,
                MalId = m.IdMal,
                Title = m.Title?.UserPreferred ?? m.Title?.English ?? m.Title?.Romaji ?? m.Title?.Native,
                Overview = m.Description,
                Status = m.Status,
                ContentRating = m.IsAdult ? "erotica" : "safe",
                PublicationYear = m.StartDate?.Year,
                TotalChapterCount = m.Chapters,
                Genres = m.Genres ?? new List<string>(),
            };

            // Primary author from staff edges role="Story" per D-21
            manga.PrimaryAuthor = m.Staff?.Edges?
                .FirstOrDefault(e => string.Equals(e.Role, "Story", StringComparison.OrdinalIgnoreCase))
                ?.Node?.Name?.Full;

            return manga;
        }
    }
}
