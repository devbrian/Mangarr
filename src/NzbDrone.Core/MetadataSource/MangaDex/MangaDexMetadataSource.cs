using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.MangaDex.Resource;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.MetadataSource.MangaDex
{
    /// <summary>
    /// First-class metadata source implementing <see cref="IProvideMangaInfo"/> +
    /// <see cref="ISearchForNewManga"/> against <c>api.mangadex.org</c>. Was the v1 default
    /// primary (D-16), but Phase 41 (41-03 D-01a) flipped <see cref="DefaultIsPrimary"/> to
    /// <c>false</c> — MangaBaka is now the default primary, and MangaDex is KEPT as a
    /// first-class fallback (D-01/D-02), not deprecated.
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
        // Bug fix (manga-details-chapter-titles, 2026-05-10): canonical Chapter.Title
        // is the English official title only. MangaDex's per-translation feed yields
        // one entry per (chapter, language, scanlation-group); non-English entries
        // routinely carry scanlator-group commentary in the Title slot (e.g. Polish
        // entries for "Solo Leveling: Ragnarok" hold lines like
        // "KONIEC DRUGIEGO SEZONU !!!!!"). Treating Chapter.Title as the canonical
        // language-neutral title (Sonarr-mirror of Episode.Title's TVDB-EN shape) means
        // only entries with translatedLanguage == "en" may contribute Title; otherwise
        // Title stays null and the UI falls back to "Chapter N". The chapter row itself
        // still ingests from any-language entry (so chapters discovered only via pl/it/etc.
        // remain enumerable + searchable) — we simply suppress the noisy non-EN title.
        private const string CanonicalTitleLanguage = "en";

        private MangaDexApi _api;

        public MangaDexMetadataSource(IHttpClient httpClient, Logger logger)
            : base(httpClient, logger)
        {
        }

        public override string Name => "MangaDex";

        public override string DefaultSourceKey => "mangadex";

        // Phase 41 D-01a: MangaBaka is now the v1.3 default primary (it carries direct
        // cross-source ids); MangaDex demotes to a non-primary default. This is the
        // matched half of D-01a — without it a fresh DB would seed TWO primaries
        // (MangaBaka + MangaDex), tripping MetadataSourceFactory.GetPrimary's
        // SingleOrDefault (Pitfall 1). MangaDex stays a first-class non-primary fallback
        // (it has a real per-chapter feed and is NOT deprecated — D-02); users can re-promote
        // it via MetadataSourceFactory.SetPrimary. This only seeds the auto-created
        // DefaultDefinition's IsPrimary flag at first startup.
        public override bool DefaultIsPrimary => false;

        // Lazily-constructed API wrapper. Ctor runs before Definition is assigned by
        // ProviderFactory (per ThingiProvider contract); we cannot read Settings in our
        // own ctor. Instead build the wrapper on first access — Settings is guaranteed
        // populated by then.
        private MangaDexApi Api =>
            _api ??= new MangaDexApi(_httpClient, Settings.BaseUrl, ResolveUserAgent, SourceKey);

        public override Tuple<NzbDrone.Core.Manga.Manga, IEnumerable<NzbDrone.Core.Manga.Chapter>>
            GetMangaInfo(string sourceId)
        {
            if (!Guid.TryParse(sourceId, out var guid))
            {
                throw new MangaNotFoundException(sourceId, $"Not a valid MangaDex GUID: {sourceId}");
            }

            var item = Api.GetById(guid)
                ?? throw new MangaNotFoundException(sourceId);

            var manga = MapManga(item);
            var feedEntries = Api.GetFeed(guid);

            // Phase 16.1 Wave 3 (REVERT-03): project the flat feed into a Sonarr-canonical
            // IEnumerable<Chapter>. MapChapters dedups multiple per-translation feed entries
            // by canonical ChapterNumber via GroupBy + Last() (BL-05 multi-translation
            // dedup safety — mirror of Sonarr's DistinctBy(new { SeasonNumber, EpisodeNumber }))
            // AND suppresses non-English titles to keep Chapter.Title canonical
            // (manga-details-chapter-titles fix, 2026-05-10).
            var chapters = MapChapters(feedEntries);
            return Tuple.Create(manga, chapters);
        }

        public override List<NzbDrone.Core.Manga.Manga> SearchForNewManga(string title)
        {
            // Use NormalizeForSearch (NOT Normalize) — MangaDex /manga?title= is a
            // tokenizing full-text search, so intra-word punctuation must become a
            // space ("Chick-Class Hunter" -> "chick class hunter") rather than
            // merging adjacent words into the non-existent token "chickclass"
            // (debug session chick-class-hunter-search-miss, 2026-06-07).
            var normalized = MangaTitleNormalizer.NormalizeForSearch(title);
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

                // Bug fix (search-titles-romanized, 2026-05-09): consult
                // attrs.AltTitles in addition to attrs.Title when picking the
                // display title. For Korean/Japanese works MangaDex routinely
                // ships attrs.Title with ONLY a romanized key (`ko-ro`/`ja-ro`)
                // and the official English title lives in attrs.AltTitles.
                // Without this, search results show "Na Honjaman Level Up:
                // Ragnarok" instead of "Solo Leveling: Ragnarok".
                Title = SelectPreferredTitle(attrs.Title, attrs.AltTitles),

                // WR-06 fix: prefer "en" only when it's non-empty; otherwise fall
                // through to the first non-empty value in the dictionary. MangaDex
                // routinely seeds an empty `en` description for new entries.
                Overview = PreferredString(attrs.Description, "en"),
                Status = attrs.Status,
                ContentRating = attrs.ContentRating,
                PublicationYear = attrs.Year,

                // Pitfall 7b (debug session add-manga-lookup-null-path, 2026-05-12):
                // attrs.LastChapter is STRING per MangaDex API (e.g. "1.2" for half-chapters);
                // parse defensively + truncate to int (TotalChapterCount is the D-21
                // similarity-bucketing axis; integer truncation is fine for that purpose).
                TotalChapterCount = ParseLastChapterCount(attrs.LastChapter),
                Genres = attrs.Tags?
                    .Select(t => (t.Attributes?.Name != null && t.Attributes.Name.TryGetValue("en", out var n))
                        ? n
                        : null)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList()
                    ?? new List<string>(),

                // GH #118 — Mangarr's analog of Sonarr's SceneMapping alias dataset.
                // Capture every alt-title string MangaDex carries (canonical title
                // dict values + every entry in attributes.altTitles), pre-normalized
                // via MangaTitleNormalizer.Normalize at write-time. Critically
                // includes the romanized attributes.title that SelectPreferredTitle
                // currently discards when an English alt-title is available — that
                // romanization is what the indexer feed emits (DEF-19-02-01 root
                // cause), so persisting it here is what restores GetManga's ability
                // to resolve search-path releases via Strategy 2 (alt-title match)
                // when the indexer-feed title diverges from the stored canonical
                // Manga.Title.
                AlternativeTitles = CollectAlternativeTitles(attrs.Title, attrs.AltTitles),
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

            // Phase 24 v1.1 — Artist from Relationships (requires includes[]=artist on the
            // request; added atomically in MangaDexApi.cs per 24-PLAN-DECISIONS Open Q #6).
            var artist = item.Relationships?.FirstOrDefault(r => r.Type == "artist");
            if (artist?.Attributes != null)
            {
                manga.Artist = artist.Attributes.Name;
            }

            // Phase 24 v1.1 — Demographic from attrs.publicationDemographic (MangaDex 4-value
            // romanization → NzbDrone.Core.Manga.MangaDemographic enum). Null / empty / unknown → null.
            manga.Demographic = MapDemographic(attrs.PublicationDemographic);

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

        // Pitfall 7b helper (debug session add-manga-lookup-null-path, 2026-05-12):
        // MangaDex returns attrs.lastChapter as a JSON STRING (e.g. "1.2", "12.5"
        // for half/extra chapters). Newtonsoft cannot coerce non-integer strings to
        // int?, which crashed the entire /api/v5/manga/lookup response on any page
        // containing a half-chapter entry. Parse via decimal.TryParse (InvariantCulture)
        // + Math.Truncate to land an int? for the D-21 multi-axis confirm
        // similarity-bucketing axis (see CrossSourceIdResolver.cs:130-135 — the math
        // is "within 10%" bucketing, not chapter-NUMBER arithmetic, so truncation is
        // the right fidelity loss). Mirrors ChapterFeedResource's string-typed Chapter
        // field convention.
        private static int? ParseLastChapterCount(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
                ? (int?)Math.Truncate(v)
                : null;
        }

        // Phase 24 v1.1 — MangaDex `publicationDemographic` 4-value romanization →
        // NzbDrone.Core.Manga.MangaDemographic enum mapping (D-04). MangaDex sends "shounen"/"shoujo" with
        // historical romanization; Mangarr's enum uses "Shonen"/"Shojo". This helper
        // absorbs the romanization variance so the enum can keep its canonical Mangarr
        // spelling. null / empty / unknown values → null (Manga.Demographic is nullable
        // and null = "not categorized" per D-04).
        private static NzbDrone.Core.Manga.MangaDemographic? MapDemographic(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return raw.ToLowerInvariant() switch
            {
                "shounen" => NzbDrone.Core.Manga.MangaDemographic.Shonen,
                "shoujo" => NzbDrone.Core.Manga.MangaDemographic.Shojo,
                "seinen" => NzbDrone.Core.Manga.MangaDemographic.Seinen,
                "josei" => NzbDrone.Core.Manga.MangaDemographic.Josei,
                _ => null,
            };
        }

        // Phase 16.1 Wave 3 (REVERT-03): each MangaDex feed entry maps to a single
        // canonical Chapter — Sonarr-canonical (mirror of SkyHookProxy.MapEpisode where each
        // feed entry produces one Episode row). The Phase 16 per-translation side-channel
        // tuple-stream projection (canonical payload + per-release feed-row) is collapsed.
        //
        // attrs.PublishAt populates Chapter.FirstReleaseDate — Sonarr-mirror of
        // Episode.AirDateUtc (Phase 16 D-02 survives the revert).
        //
        // attrs.TranslatedLanguage and the scanlation_group relationship are NOT projected
        // here — those per-release axes live on ChapterFile after import (Phase 6
        // PIPELINE-04 axis: ChapterFile.TranslatedLanguage + ChapterFile.ScanlationGroup),
        // not on the metadata-feed projection.
        //
        // Bug fix (manga-details-chapter-titles, 2026-05-10): Title is suppressed for
        // non-English feed entries — MangaDex's per-translation rows routinely carry
        // scanlator commentary in the Title slot for non-EN languages (e.g. Polish entries
        // for Solo Leveling: Ragnarok hold lines like "KONIEC DRUGIEGO SEZONU !!!!!").
        // Sonarr-mirror: Episode.Title is the canonical English title; same shape here.
        // The other fields (ChapterNumber / VolumeNumber / FirstReleaseDate / ExternalId)
        // are language-neutral structural data and ingest from any-language entry — so
        // chapters that exist only in non-EN translations are still enumerable +
        // searchable; we just don't pollute Chapter.Title with non-canonical strings.
        private static NzbDrone.Core.Manga.Chapter MapChapter(ChapterFeedEntry entry)
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

            // Suppress non-English titles to keep Chapter.Title canonical (Sonarr-mirror
            // of Episode.Title's TVDB-English shape). Polish/Italian/etc. entries
            // routinely carry scanlator-group commentary in the Title slot; ingesting
            // those as canonical Chapter.Title pollutes the Manga Details Chapters tab.
            // A null Title renders cleanly as "Chapter N" via the ChapterTitleLink
            // fallback path.
            var title = string.Equals(attrs?.TranslatedLanguage, CanonicalTitleLanguage, StringComparison.OrdinalIgnoreCase)
                ? attrs?.Title
                : null;

            return new NzbDrone.Core.Manga.Chapter
            {
                ChapterNumber = chapterNumber,
                VolumeNumber = volume,
                Title = title,
                ChapterType = ChapterType.Regular,
                FirstReleaseDate = attrs?.PublishAt,    // Sonarr-mirror of Episode.AirDateUtc (D-02 chapter-publish date)
                ExternalId = entry.Id,
                Monitored = true,                       // Sonarr precedent: new chapters monitored by default
            };
        }

        // Phase 16.1 Wave 3 (REVERT-03): flat IEnumerable<Chapter> projection — Sonarr-canonical
        // mirror of SkyHookProxy's per-feed-entry → Episode mapping with a DistinctBy precedent
        // for dedup at the natural key.
        //
        // BL-05 multi-translation regression safety: GroupBy + Last() collapses multiple
        // per-translation feed entries (the MangaDex feed yields one entry per (chapter, language,
        // group) tuple) to one canonical Chapter. Mirror of Sonarr's
        // DistinctBy(new { m.SeasonNumber, m.EpisodeNumber }) precedent at the canonical natural
        // key.
        //
        // Bug fix (manga-details-chapter-titles, 2026-05-10): ordering tweak — entries with
        // a non-null Title (i.e. the EN-language entries after the MapChapter Title-suppression
        // pass) are pushed to the END of each group so they win the Last() race. Without
        // ordering, an English entry that happens to land earlier in the feed than a non-EN
        // entry would be overwritten with a null-title row. Stable OrderBy preserves all
        // other field semantics (FirstReleaseDate / VolumeNumber / ExternalId are populated
        // from whichever entry wins; the canonical structural data is consistent across
        // per-translation entries).
        public static IEnumerable<NzbDrone.Core.Manga.Chapter> MapChapters(IEnumerable<ChapterFeedEntry> entries)
        {
            if (entries == null)
            {
                return new List<NzbDrone.Core.Manga.Chapter>();
            }

            return entries
                .Select(MapChapter)
                .GroupBy(c => c.ChapterNumber)
                .Select(g => g
                    .OrderBy(c => c.Title != null)   // false (null) sorts first, true (has-title) sorts last → Last() picks Title-bearing entry
                    .Last())
                .ToList();
        }

        /// <summary>
        /// Pick the best display title for a manga, consulting both the canonical
        /// <c>attributes.title</c> dictionary AND the <c>attributes.altTitles</c>
        /// list. Bug fix for "search results show romanized Korean/Japanese titles
        /// instead of English official titles" (search-titles-romanized, 2026-05-09).
        ///
        /// <para>Ordered preference:</para>
        /// <list type="number">
        ///   <item><description><c>title["en"]</c> when non-empty (the common path —
        ///     MangaDex's canonical English title).</description></item>
        ///   <item><description>The FIRST non-empty <c>"en"</c> entry in
        ///     <c>altTitles</c>. MangaDex maintainers list the official English
        ///     title first by convention; subsequent <c>en</c> entries are
        ///     alternate spellings or fan translations.</description></item>
        ///   <item><description>The first non-empty value of <c>title</c> as a last
        ///     resort (typically a romanization like <c>ko-ro</c> / <c>ja-ro</c>
        ///     for Korean/Japanese works) — preserves the pre-fix behavior for
        ///     records that have no English entry anywhere.</description></item>
        /// </list>
        ///
        /// <para>Returns <c>null</c> if neither dictionary nor list yields a usable
        /// string.</para>
        /// </summary>
        private static string SelectPreferredTitle(
            Dictionary<string, string> title,
            List<Dictionary<string, string>> altTitles)
        {
            // 1. canonical title["en"] wins outright.
            if (title != null
                && title.TryGetValue("en", out var canonicalEn)
                && !string.IsNullOrWhiteSpace(canonicalEn))
            {
                return canonicalEn;
            }

            // 2. First English entry in altTitles. List order is API-meaningful
            //    (maintainers list the official English title first) so we walk
            //    in declared order and take the first non-empty `en` value.
            if (altTitles != null)
            {
                foreach (var alt in altTitles)
                {
                    if (alt != null
                        && alt.TryGetValue("en", out var altEn)
                        && !string.IsNullOrWhiteSpace(altEn))
                    {
                        return altEn;
                    }
                }
            }

            // 3. Fallback: first non-empty value of the canonical title dict
            //    (typically the romanized form for Korean/Japanese works that
            //    have no English entry anywhere — better than null).
            if (title != null && title.Count > 0)
            {
                var firstNonEmpty = title.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (firstNonEmpty != null)
                {
                    return firstNonEmpty;
                }
            }

            return null;
        }

        /// <summary>
        /// GH #118 — Collect every distinct title string MangaDex carries for a
        /// manga, pre-normalized via <see cref="MangaTitleNormalizer.Normalize"/>
        /// at write-time, for storage in <see cref="NzbDrone.Core.Manga.Manga.AlternativeTitles"/>.
        ///
        /// <para>Sources consulted (in order):</para>
        /// <list type="number">
        ///   <item><description>Every non-empty value of <c>attributes.title</c> —
        ///     critically includes the romanized form (e.g. <c>"ja-ro"</c> /
        ///     <c>"ko-ro"</c>) that <see cref="SelectPreferredTitle"/> discards
        ///     when an English alt-title wins. The indexer feed emits this
        ///     romanization (DEF-19-02-01 root cause), so persisting it here
        ///     is what allows <see cref="Parser.Manga.MangaParsingService.GetManga"/>
        ///     Strategy 2 to resolve the search-path mismatch.</description></item>
        ///   <item><description>Every non-empty entry from every dictionary in
        ///     <c>attributes.altTitles</c> — across all languages. MangaDex
        ///     maintainers seed alternate spellings, fan translations, and
        ///     official titles for each supported language.</description></item>
        /// </list>
        ///
        /// <para>All values pass through <see cref="MangaTitleNormalizer.Normalize"/>
        /// before storage so <see cref="Manga.IMangaService.FindByAlternativeTitle"/>
        /// can do a direct canonical-vs-canonical comparison at read-time.
        /// Empty/whitespace results are filtered; duplicates are deduped via
        /// <see cref="Enumerable.Distinct{T}(IEnumerable{T})"/>.</para>
        /// </summary>
        private static List<string> CollectAlternativeTitles(
            Dictionary<string, string> title,
            List<Dictionary<string, string>> altTitles)
        {
            var collected = new List<string>();

            if (title != null)
            {
                foreach (var value in title.Values)
                {
                    AddNormalized(collected, value);
                }
            }

            if (altTitles != null)
            {
                foreach (var alt in altTitles)
                {
                    if (alt == null)
                    {
                        continue;
                    }

                    foreach (var value in alt.Values)
                    {
                        AddNormalized(collected, value);
                    }
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

            // Drop junk placeholders (debug alt-title-collision-guard, 2026-06-19) so they
            // never enter AlternativeTitles and become a cross-title resolution key;
            // IsJunkPlaceholder also covers the empty/whitespace case checked previously.
            if (!MangaTitleNormalizer.IsJunkPlaceholder(normalized))
            {
                bucket.Add(normalized);
            }
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
