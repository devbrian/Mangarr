using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly IAniListGraphQlTransport _transport;

        public AniListMetadataSource(IHttpClient httpClient, IAniListGraphQlTransport transport, Logger logger)
            : base(httpClient, logger)
        {
            _transport = transport;
        }

        public override string Name => "AniList";

        public override string DefaultSourceKey => "anilist";

        public override bool DefaultIsPrimary => false;     // D-16 — secondary fallback

        public override Tuple<Manga.Manga, IEnumerable<Chapter>> GetMangaInfo(string sourceId)
        {
            if (!int.TryParse(sourceId, out var anilistId))
            {
                throw new MangaNotFoundException(sourceId, $"Not a valid AniList integer ID: {sourceId}");
            }

            var media = QueryMediaById(anilistId)
                ?? throw new MangaNotFoundException(sourceId);

            // AniList exposes NO per-chapter feed in v1; return EMPTY chapter list. Phase 16.1
            // Wave 3 (REVERT-03): shape conforms to the Sonarr-canonical flat IEnumerable<Chapter>;
            // when AniList is the active primary the RefreshMangaService creates zero canonical
            // Chapters, which renders the manga as fully-Missing. MangaDex remains the v1 default
            // primary (D-16) so this path is the dormant fallback.
            return Tuple.Create(MapManga(media), Enumerable.Empty<Chapter>());
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
            var resp = _transport.Post<MediaResponseShape>(body);
            return resp?.Data?.Media;
        }

        private AniListPage QueryMediaSearch(string search)
        {
            var body = AniListMangaApi.BuildBody(AniListMangaApi.MediaSearchQuery, new { search, page = 1 });
            var resp = _transport.Post<PageResponseShape>(body);
            return resp?.Data?.Page;
        }

        private AniListPage QueryMediaByIdMal(int malId)
        {
            var body = AniListMangaApi.BuildBody(AniListMangaApi.MediaByIdMalQuery, new { idMal = malId });
            var resp = _transport.Post<MediaResponseShape>(body);
            return resp?.Data?.Media != null
                ? new AniListPage { Media = new List<AniListMedia> { resp.Data.Media } }
                : null;
        }

        // Phase 26 Plan 26-02 (IL-07): the prior in-class GraphQL HTTP helper was lifted
        // verbatim into AniListGraphQlTransport (DI-injected as _transport above). Phase 27
        // AniListImportList will share the same transport instance via DryIoc auto-wiring.

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

                // GH #118 — Mangarr's analog of Sonarr's SceneMapping alias dataset.
                // AniList exposes four title variants (romaji / english / native /
                // userPreferred) plus a synonyms list. All entries pre-normalized via
                // MangaTitleNormalizer.Normalize at write-time. Consumed by
                // MangaParsingService.GetManga Strategy 2 (alt-title match) to resolve
                // search-path releases whose feed-title diverges from the stored
                // Manga.Title pick.
                AlternativeTitles = CollectAlternativeTitles(m.Title, m.Synonyms),
            };

            // Primary author from staff edges role="Story" per D-21
            manga.PrimaryAuthor = m.Staff?.Edges?
                .FirstOrDefault(e => string.Equals(e.Role, "Story", StringComparison.OrdinalIgnoreCase))
                ?.Node?.Name?.Full;

            return manga;
        }

        /// <summary>
        /// GH #118 — Collect every distinct AniList title variant + synonym,
        /// pre-normalized via <see cref="MangaTitleNormalizer.Normalize"/>, for
        /// storage in <see cref="NzbDrone.Core.Manga.Manga.AlternativeTitles"/>.
        /// Sources: <c>title.romaji</c>, <c>title.english</c>, <c>title.native</c>,
        /// <c>title.userPreferred</c>, and every entry in <c>synonyms</c>.
        /// </summary>
        private static List<string> CollectAlternativeTitles(AniListTitleBlock title, List<string> synonyms)
        {
            var collected = new List<string>();

            if (title != null)
            {
                AddNormalized(collected, title.Romaji);
                AddNormalized(collected, title.English);
                AddNormalized(collected, title.Native);
                AddNormalized(collected, title.UserPreferred);
            }

            if (synonyms != null)
            {
                foreach (var syn in synonyms)
                {
                    AddNormalized(collected, syn);
                }
            }

            return collected.Distinct().ToList();
        }

        private static void AddNormalized(List<string> bucket, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var normalized = MangaTitleNormalizer.Normalize(raw);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                bucket.Add(normalized);
            }
        }
    }
}
