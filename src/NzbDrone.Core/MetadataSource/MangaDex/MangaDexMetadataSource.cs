using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.MangaDex.Resource;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.MetadataSource.MangaDex
{
    /// <summary>
    /// v1 PRIMARY metadata source per D-16 — ships with <see cref="DefaultIsPrimary"/> = true.
    /// Implements <see cref="IProvideMangaInfo"/> + <see cref="ISearchForNewManga"/> against
    /// <c>api.mangadex.org</c>.
    ///
    /// <para>
    /// Honest UA per Phase 1 D-13/D-14 + MangaDex ToS. <see cref="MangaDexMetadataSourceSettings"/>
    /// holds <c>UserAgentOverride</c> for interface-contract reasons but does NOT expose it via
    /// <c>[FieldDefinition]</c> — there is no UI affordance to spoof the honest
    /// <c>Mangarr/{version}</c> UA (T-CONFIG-DRIFT-01 mitigation by absence).
    /// </para>
    ///
    /// <para>
    /// PITFALL 7: <c>links.al</c> / <c>links.mal</c> arrive as JSON STRING values from
    /// MangaDex; <see cref="MapManga"/> runs <see cref="int.TryParse(string, out int)"/>
    /// before persisting to <see cref="NzbDrone.Core.Manga.Manga.AniListId"/> /
    /// <see cref="NzbDrone.Core.Manga.Manga.MalId"/>.
    /// </para>
    ///
    /// <para>
    /// Cross-source reverse lookup (Search-by-AniListId / Search-by-MalId) is NOT supported
    /// by MangaDex's API directly. The Plan 02-09 <c>CrossSourceIdResolver</c> wires the
    /// fuzzy fallback (≥0.85 Jaro-Winkler + 2-of-3 multi-axis confirm via D-19..D-22) on top
    /// of <see cref="SearchForNewManga(string)"/>; the by-cross-id overloads return empty
    /// here as a documented capability gap.
    /// </para>
    /// </summary>
    public class MangaDexMetadataSource : HttpMetadataSourceBase<MangaDexMetadataSourceSettings>
    {
        private MangaDexApi _api;

        public MangaDexMetadataSource(IHttpClient httpClient, Logger logger)
            : base(httpClient, logger)
        {
        }

        public override string Name => "MangaDex";

        public override string DefaultSourceKey => "mangadex";

        // D-16: ships as v1 default primary. Users can promote AniList or MAL via
        // MetadataSourceFactory.SetPrimary; this only seeds the auto-created
        // DefaultDefinition's IsPrimary flag at first startup.
        public override bool DefaultIsPrimary => true;

        // Lazily-constructed API wrapper. Ctor runs before Definition is assigned by
        // ProviderFactory (per ThingiProvider contract); we cannot read Settings in our
        // own ctor. Instead build the wrapper on first access — Settings is guaranteed
        // populated by then.
        private MangaDexApi Api =>
            _api ??= new MangaDexApi(_httpClient, Settings.BaseUrl, ResolveUserAgent, SourceKey);

        public override Tuple<NzbDrone.Core.Manga.Manga, List<Chapter>> GetMangaInfo(string sourceId)
        {
            if (!Guid.TryParse(sourceId, out var guid))
            {
                throw new MangaNotFoundException(sourceId, $"Not a valid MangaDex GUID: {sourceId}");
            }

            var item = Api.GetById(guid)
                ?? throw new MangaNotFoundException(sourceId);

            var manga = MapManga(item);
            var feedEntries = Api.GetFeed(guid);
            var chapters = feedEntries.Select(MapChapter).ToList();
            return Tuple.Create(manga, chapters);
        }

        public override List<NzbDrone.Core.Manga.Manga> SearchForNewManga(string title)
        {
            var normalized = MangaTitleNormalizer.Normalize(title);
            return Api.Search(normalized).Select(MapManga).ToList();
        }

        public override List<NzbDrone.Core.Manga.Manga> SearchForNewMangaByMangaDexId(string mangaDexId)
        {
            try
            {
                return new List<NzbDrone.Core.Manga.Manga> { GetMangaInfo(mangaDexId).Item1 };
            }
            catch (MangaNotFoundException)
            {
                return new List<NzbDrone.Core.Manga.Manga>();
            }
        }

        // MangaDex /manga search by AniList/MAL ID is NOT supported via direct query.
        // CrossSourceIdResolver (Plan 02-09) handles cross-source reverse lookup via
        // fuzzy title match + multi-axis confirm. Returning empty is the documented
        // capability gap — NOT a swallowed failure.
        public override List<NzbDrone.Core.Manga.Manga> SearchForNewMangaByAniListId(int aniListId)
            => new List<NzbDrone.Core.Manga.Manga>();

        public override List<NzbDrone.Core.Manga.Manga> SearchForNewMangaByMalId(int malId)
            => new List<NzbDrone.Core.Manga.Manga>();

        public override ValidationResult Test()
        {
            // WR-17 fix: use the lightweight /ping endpoint instead of Api.Search("test").
            // /ping returns "pong" plain text and does not count against the 40 req/min
            // search budget the way Search does. A user mashing the Settings → Test
            // button no longer drains the active refresh budget.
            try
            {
                if (!Api.Ping())
                {
                    return new ValidationResult(new[]
                    {
                        new ValidationFailure("BaseUrl", "MangaDex /ping returned non-success"),
                    });
                }

                return new ValidationResult();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "MangaDex Test failed");
                return new ValidationResult(new[]
                {
                    new ValidationFailure("BaseUrl", ex.Message),
                });
            }
        }

        // ---- Mapping helpers ----

        private static NzbDrone.Core.Manga.Manga MapManga(MangaDataItem item)
        {
            var attrs = item.Attributes ?? new MangaAttributes();
            var manga = new NzbDrone.Core.Manga.Manga
            {
                MangaDexId = Guid.TryParse(item.Id, out var g) ? g : (Guid?)null,
                Title = PreferredTitle(attrs.Title),

                // WR-06 fix: prefer "en" only when it's non-empty; otherwise fall
                // through to the first non-empty value in the dictionary. MangaDex
                // routinely seeds an empty `en` description for new entries.
                Overview = PreferredString(attrs.Description, "en"),
                Status = attrs.Status,
                ContentRating = attrs.ContentRating,
                PublicationYear = attrs.Year,
                TotalChapterCount = attrs.LastChapter,
                Genres = attrs.Tags?
                    .Select(t => (t.Attributes?.Name != null && t.Attributes.Name.TryGetValue("en", out var n))
                        ? n
                        : null)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList()
                    ?? new List<string>(),
            };

            // PER PITFALL 7: links values are JSON STRINGS (not ints). Run int.TryParse
            // before persisting — silently skip when parse fails (legacy MangaDex records
            // occasionally store slugs in these fields).
            if (attrs.Links != null)
            {
                if (attrs.Links.TryGetValue("al", out var alStr) && int.TryParse(alStr, out var anilistId))
                {
                    manga.AniListId = anilistId;
                }

                if (attrs.Links.TryGetValue("mal", out var malStr) && int.TryParse(malStr, out var malId))
                {
                    manga.MalId = malId;
                }
            }

            // Primary author from Relationships (requires includes[]=author on the request).
            var author = item.Relationships?.FirstOrDefault(r => r.Type == "author");
            if (author?.Attributes != null)
            {
                manga.PrimaryAuthor = author.Attributes.Name;
            }

            // Sonarr divergence: Phase 15 Plan 15-12 fix-forward — populate Manga.Images
            // from the cover_art relationship. Without this, /api/v5/manga and
            // /api/v5/manga/lookup return empty `images: []` arrays and every UI poster
            // falls back to the placeholder PNG (F-12 from 2026-05-08 smoke test).
            //
            // MangaDex cover URL pattern (per https://api.mangadex.org/docs):
            //   https://uploads.mangadex.org/covers/{manga-id}/{filename}
            // Thumbnails: append `.512.jpg` or `.256.jpg` for sized variants. The
            // MangaImage.tsx frontend hook already does the size-suffix substitution
            // (`poster.jpg` → `poster-{size}.jpg`); we register the full-resolution URL
            // here. MangaMediaCoverService.ConvertToLocalUrls(0, ...) rewrites it to
            // /MediaCoverProxy/{hash}/poster.jpg for unsaved manga and to
            // /MediaCover/manga/{id}/poster.jpg for saved manga (after the cover-download
            // job runs). The downloader fetches `RemoteUrl` so it must be the canonical
            // CDN URL.
            var coverArt = item.Relationships?.FirstOrDefault(r => r.Type == "cover_art");
            if (!string.IsNullOrEmpty(coverArt?.Attributes?.FileName) && Guid.TryParse(item.Id, out var mangaGuid))
            {
                var coverUrl = $"https://uploads.mangadex.org/covers/{mangaGuid}/{coverArt.Attributes.FileName}";
                manga.Images = new List<NzbDrone.Core.MediaCover.MediaCover>
                {
                    new NzbDrone.Core.MediaCover.MediaCover(MediaCoverTypes.Poster, coverUrl),
                };
            }

            return manga;
        }

        private static Chapter MapChapter(ChapterFeedEntry entry)
        {
            var attrs = entry.Attributes;
            var chapterNumber = 0m;
            if (attrs?.Chapter != null)
            {
                decimal.TryParse(attrs.Chapter, NumberStyles.Number, CultureInfo.InvariantCulture, out chapterNumber);
            }

            int? volume = null;
            if (attrs?.Volume != null && int.TryParse(attrs.Volume, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                volume = v;
            }

            var group = entry.Relationships?
                .FirstOrDefault(r => r.Type == "scanlation_group")?.Attributes?.Name;

            return new Chapter
            {
                ChapterNumber = chapterNumber,
                VolumeNumber = volume,
                Title = attrs?.Title,
                TranslatedLanguage = attrs?.TranslatedLanguage ?? "und",
                ScanlationGroup = group,
                ChapterType = ChapterType.Regular,
                IsSynthetic = false,
                ReleaseDate = attrs?.PublishAt,
                ExternalId = entry.Id,
                Monitored = true,
            };
        }

        private static string PreferredTitle(Dictionary<string, string> titles)
        {
            // WR-06 mirror: prefer "en" only when non-empty; otherwise fall through
            // to the first non-empty value. Same MangaDex empty-en gotcha as on
            // Description.
            return PreferredString(titles, "en");
        }

        private static string PreferredString(Dictionary<string, string> bag, string preferredKey)
        {
            if (bag == null || bag.Count == 0)
            {
                return null;
            }

            if (bag.TryGetValue(preferredKey, out var preferred) && !string.IsNullOrWhiteSpace(preferred))
            {
                return preferred;
            }

            return bag.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }
    }
}
