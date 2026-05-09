using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MetadataSource.MyAnimeList.Resource;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.MetadataSource.MyAnimeList
{
    /// <summary>
    /// MyAnimeList v2 metadata source per D-24 (client-ID-only auth, NOT OAuth) and D-16
    /// (secondary by default — MangaDex is the v1 primary). Implements
    /// <see cref="IMetadataSource"/> via the <see cref="HttpMetadataSourceBase{TSettings}"/>
    /// scaffold (Plan 02-05). Concrete API plumbing lives in <see cref="MalApi"/>;
    /// this class handles search/info contract + DTO-to-domain mapping (D-21 axes).
    /// </summary>
    public class MyAnimeListMetadataSource : HttpMetadataSourceBase<MyAnimeListMetadataSourceSettings>
    {
        private MalApi _api;

        public MyAnimeListMetadataSource(IHttpClient httpClient, Logger logger)
            : base(httpClient, logger)
        {
        }

        public override string Name => "MyAnimeList";

        public override string DefaultSourceKey => "myanimelist";

        public override bool DefaultIsPrimary => false;     // D-16 — secondary fallback

        // Lazy: Definition.Settings is bound after ctor, so the api can't be built eagerly.
        private MalApi Api => _api ??= new MalApi(
            _httpClient,
            Settings.BaseUrl,
            ResolveUserAgent,
            () => Settings.ClientId,
            SourceKey);

        public override Tuple<Manga.Manga, IEnumerable<(decimal ChapterNumber, ChapterEnsureInputs Canonical, List<ChapterReleaseFeedRow> Releases)>>
            GetMangaInfo(string sourceId)
        {
            if (!int.TryParse(sourceId, out var malId))
            {
                throw new MangaNotFoundException(sourceId, $"Not a valid MAL integer ID: {sourceId}");
            }

            var resource = Api.GetById(malId)
                ?? throw new MangaNotFoundException(sourceId);

            // MAL exposes NO per-chapter feed in v2; return EMPTY tuple stream. Phase 16
            // STRUCT-05 + STRUCT-07: shape conforms to the canonical/release tuple stream;
            // when MAL is the active primary the post-Phase-16 RefreshMangaService creates
            // zero canonical Chapters, which renders the manga as fully-Missing (D-04). MangaDex
            // remains the v1 default primary (D-16) so this path is the dormant fallback.
            return Tuple.Create(MapManga(resource),
                Enumerable.Empty<(decimal, ChapterEnsureInputs, List<ChapterReleaseFeedRow>)>());
        }

        public override List<Manga.Manga> SearchForNewManga(string title)
        {
            var normalized = MangaTitleNormalizer.Normalize(title);
            return Api.Search(normalized).Select(MapManga).ToList();
        }

        public override List<Manga.Manga> SearchForNewMangaByMangaDexId(string mangaDexId) => new();

        public override List<Manga.Manga> SearchForNewMangaByAniListId(int aniListId) => new();

        public override List<Manga.Manga> SearchForNewMangaByMalId(int malId)
        {
            try
            {
                return new List<Manga.Manga> { GetMangaInfo(malId.ToString()).Item1 };
            }
            catch (MangaNotFoundException)
            {
                return new List<Manga.Manga>();
            }
        }

        public override ValidationResult Test()
        {
            if (string.IsNullOrWhiteSpace(Settings.ClientId))
            {
                return new ValidationResult(new[]
                {
                    new ValidationFailure("ClientId", "MAL API Client ID required (https://myanimelist.net/apiconfig)"),
                });
            }

            try
            {
                // WR-17 fix: fetch a single known-stable manga (id=1 = "Monster", a
                // canonical long-running MAL entry) instead of issuing a full Search
                // query that hits the search rate budget. The result is discarded;
                // we only need to confirm the X-MAL-CLIENT-ID auth round-trip works.
                Api.GetById(1);
                return new ValidationResult();
            }
            catch (Exception ex)
            {
                return new ValidationResult(new[] { new ValidationFailure("ClientId", ex.Message) });
            }
        }

        // ---- Mapping ----
        private Manga.Manga MapManga(MalMangaResource r)
        {
            var manga = new Manga.Manga
            {
                MalId = r.Id,
                Title = r.Title,
                Overview = r.Synopsis,
                Status = r.Status,
                ContentRating = MapContentRating(r.Nsfw),
                PublicationYear = ParseYear(r.StartDate),
                TotalChapterCount = r.NumChapters,
                Genres = r.Genres?.Select(g => g.Name).ToList() ?? new List<string>(),
            };

            // Primary author from authors edges role="Story" per D-21
            var storyAuthor = r.Authors?.FirstOrDefault(a => string.Equals(a.Role, "Story", StringComparison.OrdinalIgnoreCase));
            if (storyAuthor?.Node != null)
            {
                manga.PrimaryAuthor = $"{storyAuthor.Node.FirstName} {storyAuthor.Node.LastName}".Trim();
            }

            return manga;
        }

        private static string MapContentRating(string nsfw)
        {
            // MAL nsfw axis: white | gray | black -> safe | suggestive | pornographic
            if (string.Equals(nsfw, "white", StringComparison.OrdinalIgnoreCase))
            {
                return "safe";
            }

            if (string.Equals(nsfw, "black", StringComparison.OrdinalIgnoreCase))
            {
                return "pornographic";
            }

            return "suggestive";
        }

        private static int? ParseYear(string isoDate)
        {
            if (string.IsNullOrEmpty(isoDate) || isoDate.Length < 4)
            {
                return null;
            }

            return int.TryParse(isoDate.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
                ? y
                : (int?)null;
        }
    }
}
