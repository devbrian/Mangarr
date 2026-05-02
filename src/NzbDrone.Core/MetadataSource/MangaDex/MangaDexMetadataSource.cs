using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Manga;
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
            try
            {
                Api.Search("test");
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
                Overview = (attrs.Description != null && attrs.Description.TryGetValue("en", out var en))
                    ? en
                    : attrs.Description?.Values.FirstOrDefault(),
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
            if (titles == null || titles.Count == 0)
            {
                return null;
            }

            return titles.TryGetValue("en", out var en) ? en : titles.Values.First();
        }
    }
}
