using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.MangaBaka.Resource;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.MetadataSource.MangaBaka
{
    /// <summary>
    /// MangaBaka metadata source — Phase 41 sibling of <see cref="MangaDex.MangaDexMetadataSource"/>.
    /// Implements <see cref="IProvideMangaInfo"/> + <see cref="ISearchForNewManga"/> against
    /// <c>api.mangabaka.org</c>.
    ///
    /// <para>
    /// v1.3 DEFAULT PRIMARY per D-01a — ships with <see cref="DefaultIsPrimary"/> = true.
    /// MangaDex's <c>DefaultIsPrimary</c> is simultaneously flipped to false so a fresh DB
    /// seeds exactly ONE primary (Pitfall 1). Both providers stay visible in the Settings
    /// Add picker — neither provider overrides the deprecation flag (D-02): MangaDex
    /// remains a first-class fallback because it has a real per-chapter feed.
    /// </para>
    ///
    /// <para>
    /// NO per-chapter feed: MangaBaka exposes only a <c>total_chapters</c> count, never a
    /// chapter listing. <see cref="GetMangaInfo"/> therefore SYNTHESIZES a whole-number
    /// catalog <c>1..total_chapters</c> (D-02/D-04) — clamped at <see cref="MaxWholeCap"/>
    /// (D-05 DoS guard) since <c>total_chapters</c> is untrusted upstream. Synthesized rows
    /// carry provenance ExternalIds of the form <c>mangabaka:[id]:c[n]</c> and Monitored=true (D-06 —
    /// the refresh path does NOT re-run ChapterMonitoredService, mirroring MapChapter).
    /// </para>
    ///
    /// <para>
    /// DIRECT cross-source ids (D-03/D-08-R): MangaBaka ships <c>source.anilist.id</c> and
    /// <c>source.my_anime_list.id</c> as RAW INTEGERS, so <see cref="MapManga"/> reads them
    /// straight into <see cref="NzbDrone.Core.Manga.Manga.AniListId"/> /
    /// <see cref="NzbDrone.Core.Manga.Manga.MalId"/> — short-circuiting the Jaro-Winkler
    /// CrossSourceIdResolver and the cross-title mis-attribution it risks (T-41-XID;
    /// load-bearing for Phase-40 gateway grab attribution). MangaDexId stays null (D-03a).
    /// </para>
    ///
    /// <para>
    /// Honest UA per Phase 1 D-13/D-14 — <see cref="MangaBakaMetadataSourceSettings"/> holds
    /// <c>UserAgentOverride</c> for the interface contract but does NOT expose it via
    /// <c>[FieldDefinition]</c> (T-CONFIG-DRIFT-01 mitigation by absence).
    /// </para>
    /// </summary>
    public class MangaBakaMetadataSource : HttpMetadataSourceBase<MangaBakaMetadataSourceSettings>
    {
        // DoS expansion guard mirroring ChapterSynthesisService.MaxWholeCap (Manga/ChapterSynthesisService.cs:29).
        // total_chapters is remote-controlled (untrusted upstream); an absurd value could otherwise
        // drive an unbounded 1..N allocation. Clamp + Warn before the loop (D-05 / T-41).
        private const int MaxWholeCap = 5000;

        private MangaBakaApi _api;

        public MangaBakaMetadataSource(IHttpClient httpClient, Logger logger)
            : base(httpClient, logger)
        {
        }

        public override string Name => "MangaBaka";

        public override string DefaultSourceKey => "mangabaka";

        // D-01a: v1.3 default primary. Paired with MangaDexMetadataSource.DefaultIsPrimary => false
        // so a fresh-DB seed yields exactly one primary (Pitfall 1). Users can promote MangaDex
        // (or any other source) via MetadataSourceFactory.SetPrimary.
        public override bool DefaultIsPrimary => true;

        // Lazily-constructed API wrapper. Ctor runs before Definition is assigned by
        // ProviderFactory (per ThingiProvider contract); we cannot read Settings in our
        // own ctor. Build the wrapper on first access — Settings is populated by then.
        private MangaBakaApi Api =>
            _api ??= new MangaBakaApi(_httpClient, Settings.BaseUrl, ResolveUserAgent, SourceKey);

        public override Tuple<NzbDrone.Core.Manga.Manga, IEnumerable<NzbDrone.Core.Manga.Chapter>>
            GetMangaInfo(string sourceId)
        {
            if (!int.TryParse(sourceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                throw new MangaNotFoundException(sourceId, $"Not a valid MangaBaka id: {sourceId}");
            }

            var record = Api.GetById(id)
                ?? throw new MangaNotFoundException(sourceId);

            var manga = MapManga(record);

            // No per-chapter feed exists (D-02) — synthesize a whole-number 1..total_chapters
            // catalog with provenance ExternalIds instead of reading a feed.
            var chapters = SynthesizeChapters(id, record.TotalChapters);
            return Tuple.Create(manga, chapters);
        }

        public override List<NzbDrone.Core.Manga.Manga> SearchForNewManga(string title)
        {
            // Use NormalizeForSearch (NOT Normalize) — the same tokenizing-full-text rationale
            // as MangaDexMetadataSource.SearchForNewManga (intra-word punctuation must become a
            // space rather than merging adjacent words into a non-existent token).
            var normalized = MangaTitleNormalizer.NormalizeForSearch(title);
            return Api.Search(normalized).Select(MapManga).ToList();
        }

        /// <summary>
        /// Native int-by-id lookup. The <paramref name="mangaBakaId"/> parameter NAME is
        /// legacy (the <see cref="ISearchForNewManga"/> contract slot was minted for MangaDex's
        /// GUID-string id and is reused verbatim per RESEARCH Open Q1 — we do NOT widen the
        /// interface, which has a 4-provider blast radius). For MangaBaka the behavior is a
        /// provider-defined int-by-id fetch: parse the string as an int, GetById, return the
        /// single record (empty list on MangaNotFoundException).
        /// </summary>
        public override List<NzbDrone.Core.Manga.Manga> SearchForNewMangaByMangaDexId(string mangaBakaId)
        {
            try
            {
                return new List<NzbDrone.Core.Manga.Manga> { GetMangaInfo(mangaBakaId).Item1 };
            }
            catch (MangaNotFoundException)
            {
                return new List<NzbDrone.Core.Manga.Manga>();
            }
        }

        // MangaBaka exposes no AniList/MAL reverse-lookup endpoint. Returning empty is the
        // documented capability gap (D-07) — NOT a swallowed failure. CrossSourceIdResolver
        // is NOT consulted here: MangaBaka already carries the direct ids on the record
        // (see MapManga), so the fuzzy fallback is unnecessary for this provider.
        public override List<NzbDrone.Core.Manga.Manga> SearchForNewMangaByAniListId(int aniListId)
            => new List<NzbDrone.Core.Manga.Manga>();

        public override List<NzbDrone.Core.Manga.Manga> SearchForNewMangaByMalId(int malId)
            => new List<NzbDrone.Core.Manga.Manga>();

        public override ValidationResult Test()
        {
            // MangaBaka has no /ping endpoint; use a canned search (D-09). The honest UA + the
            // mangabaka rate budget apply. No secret is ever surfaced in a failure message —
            // the Settings POCO carries no credential field.
            try
            {
                Api.Search("test");
                return new ValidationResult();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "MangaBaka Test failed");
                return new ValidationResult(new[]
                {
                    new ValidationFailure("BaseUrl", ex.Message),
                });
            }
        }

        // ---- Mapping helpers ----

        private static NzbDrone.Core.Manga.Manga MapManga(MangaBakaSeries record)
        {
            var manga = new NzbDrone.Core.Manga.Manga
            {
                MangaBakaId = record.Id,
                Title = SelectPreferredTitle(record),
                Overview = record.Description,
                Status = record.Status,
                ContentRating = record.ContentRating,
                PublicationYear = record.Year,
                Genres = record.Genres ?? new List<string>(),
                PrimaryAuthor = record.Authors?.FirstOrDefault(),
                Artist = record.Artists?.FirstOrDefault(),
                TotalChapterCount = ParseTotalChapters(record.TotalChapters),
                AlternativeTitles = CollectAlternativeTitles(record),
                Demographic = MapDemographic(record.Genres),
            };

            // Cover: MangaBaka pre-resolves the URL (cover.raw.url) — no CDN-path assembly
            // (simpler than MangaDexMetadataSource which builds the uploads.mangadex.org URL).
            if (record.Cover?.Raw?.Url is string coverUrl && !string.IsNullOrWhiteSpace(coverUrl))
            {
                manga.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover(MediaCoverTypes.Poster, coverUrl),
                };
            }

            // Direct cross-source ids (D-03/D-08-R): MangaBaka ships these as RAW INTEGERS,
            // so read them straight in — no int.TryParse on a string (the MangaDex Pitfall 7
            // path), and short-circuiting CrossSourceIdResolver entirely (T-41-XID). MangaDexId
            // stays null (D-03a): a MangaBaka-sourced manga is not a MangaDex record.
            if (record.Source?.AniList?.Id is int aniListId)
            {
                manga.AniListId = aniListId;
            }

            if (record.Source?.MyAnimeList?.Id is int malId)
            {
                manga.MalId = malId;
            }

            return manga;
        }

        // MangaBaka ships the publication demographic INSIDE the flat `genres` array
        // (e.g. ["action","adventure","shounen"]) rather than as a dedicated field —
        // there is no `publicationDemographic` peer like MangaDex's. Scan the genres for
        // the first demographic term and map it to the MangaDemographic enum so the
        // DemographicSpecification auto-tagging path works for MangaBaka-sourced manga
        // (parity with MangaDexMetadataSource.MapDemographic). Both common romanizations
        // are accepted (shounen/shonen, shoujo/shojo). Returns null when no demographic
        // term is present (the "not categorized" sentinel — Manga.Demographic is nullable).
        private static NzbDrone.Core.Manga.MangaDemographic? MapDemographic(List<string> genres)
        {
            if (genres == null)
            {
                return null;
            }

            foreach (var genre in genres)
            {
                switch (genre?.Trim().ToLowerInvariant())
                {
                    case "shounen":
                    case "shonen":
                        return NzbDrone.Core.Manga.MangaDemographic.Shonen;
                    case "shoujo":
                    case "shojo":
                        return NzbDrone.Core.Manga.MangaDemographic.Shojo;
                    case "seinen":
                        return NzbDrone.Core.Manga.MangaDemographic.Seinen;
                    case "josei":
                        return NzbDrone.Core.Manga.MangaDemographic.Josei;
                }
            }

            return null;
        }

        // Clone of MangaDexMetadataSource.ParseLastChapterCount (D-04): total_chapters is a
        // JSON STRING (Pitfall 2) that Newtonsoft cannot coerce to int? (e.g. "1.2"
        // half-chapters, or empty). Parse defensively via decimal.TryParse(InvariantCulture)
        // + Math.Truncate; null on null/empty/non-integer.
        private static int? ParseTotalChapters(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
                ? (int?)Math.Truncate(v)
                : null;
        }

        // Synthesize a whole-number 1..total_chapters catalog (D-02 — MangaBaka has no
        // per-chapter feed). null/empty/"0"/non-integer → empty (D-04). Clamp at MaxWholeCap
        // with a Warn (D-05). Row field shape mirrors MangaDexMetadataSource.MapChapter
        // (D-06): provenance ExternalId, Monitored=true (refresh path does NOT re-run the
        // monitor policy layer), null Title/VolumeNumber/FirstReleaseDate, Regular type.
        private IEnumerable<NzbDrone.Core.Manga.Chapter> SynthesizeChapters(int mangaBakaId, string totalChaptersRaw)
        {
            var parsed = ParseTotalChapters(totalChaptersRaw);
            if (parsed is not int n || n <= 0)
            {
                return Enumerable.Empty<NzbDrone.Core.Manga.Chapter>();
            }

            if (n > MaxWholeCap)
            {
                _logger.Warn(
                    "MangaBaka chapter synthesis for id={0} capped {1} to {2} (DoS guard).",
                    mangaBakaId,
                    n,
                    MaxWholeCap);
                n = MaxWholeCap;
            }

            var chapters = new List<NzbDrone.Core.Manga.Chapter>(n);
            for (var i = 1; i <= n; i++)
            {
                chapters.Add(new NzbDrone.Core.Manga.Chapter
                {
                    ChapterNumber = i,
                    ExternalId = $"mangabaka:{mangaBakaId}:c{i}",
                    Title = null,
                    VolumeNumber = null,
                    FirstReleaseDate = null,
                    ChapterType = ChapterType.Regular,
                    Monitored = true,
                });
            }

            return chapters;
        }

        // Pick the best display title. MangaBaka exposes named title fields (vs MangaDex's
        // language-keyed dictionaries): `title` is the canonical (typically English) title,
        // `romanized_title` the romanization, `native_title` the native-script form. Prefer
        // the canonical title, then romanized, then native, then the first secondary title.
        // (titles[] entries carry only language + primary flag, no title string, so they
        // cannot contribute here.)
        private static string SelectPreferredTitle(MangaBakaSeries record)
        {
            if (!string.IsNullOrWhiteSpace(record.Title))
            {
                return record.Title;
            }

            if (!string.IsNullOrWhiteSpace(record.RomanizedTitle))
            {
                return record.RomanizedTitle;
            }

            if (!string.IsNullOrWhiteSpace(record.NativeTitle))
            {
                return record.NativeTitle;
            }

            if (record.SecondaryTitles != null)
            {
                var firstNonEmpty = record.SecondaryTitles.Values
                    .Where(list => list != null)
                    .SelectMany(list => list)
                    .Select(entry => entry?.Title)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                if (firstNonEmpty != null)
                {
                    return firstNonEmpty;
                }
            }

            return null;
        }

        // GH #118 analog (mirror of MangaDexMetadataSource.CollectAlternativeTitles): harvest
        // every title string MangaBaka carries (title / native_title / romanized_title /
        // secondary_titles values), pre-normalized via MangaTitleNormalizer.Normalize at
        // write-time so MangaParsingService.GetManga Strategy 2 can do an exact canonical
        // match against indexer-feed release titles. (titles[] entries carry no title string —
        // language + primary flag only — so there is nothing to harvest from that array.)
        private static List<string> CollectAlternativeTitles(MangaBakaSeries record)
        {
            var collected = new List<string>();

            AddNormalized(collected, record.Title);
            AddNormalized(collected, record.NativeTitle);
            AddNormalized(collected, record.RomanizedTitle);

            if (record.SecondaryTitles != null)
            {
                foreach (var entry in record.SecondaryTitles.Values
                             .Where(list => list != null)
                             .SelectMany(list => list))
                {
                    AddNormalized(collected, entry?.Title);
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
