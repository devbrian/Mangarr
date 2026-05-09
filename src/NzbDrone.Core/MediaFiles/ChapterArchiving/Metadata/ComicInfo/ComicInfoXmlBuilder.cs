using System.Globalization;
using System.Xml.Linq;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo
{
    /// <summary>
    /// Phase 4 ARCHIVE-04 — pure ComicInfo.xml XDocument builder.
    /// No I/O; testable in isolation against golden XML fixtures.
    ///
    /// Dual-write contract (Pitfall 3): emits BOTH v2.0 &lt;ScanInformation&gt; AND v2.1
    /// &lt;Translator&gt; so Kavita (ambiguous v2.1 support) sees the v2.0 fallback while
    /// Komga 1.10+ sees the preferred v2.1 field. Reader apps tolerate unknown elements
    /// per the XSD's lack of strictness.
    ///
    /// Pitfall 7 mitigation: ALL numeric formatting uses
    /// <see cref="CultureInfo.InvariantCulture"/> so de-DE / ja-JP locales don't
    /// introduce comma-decimal drift on chapter numbers, year/month/day.
    ///
    /// No xmlns declaration — anansi-project XSDs define no targetNamespace [VERIFIED 2026-05-02].
    ///
    /// XML element-value escaping (T-04-16 mitigation): <see cref="XElement"/> auto-escapes
    /// element text on serialize; aggregator-supplied content (Title / Overview / ScanlationGroup /
    /// TranslatedLanguage) flows through <c>new XElement(name, value)</c> with no string-concat into
    /// raw XML, so <c>&lt;</c>, <c>&amp;</c>, <c>&apos;</c>, <c>&quot;</c> are escaped automatically.
    /// </summary>
    public static class ComicInfoXmlBuilder
    {
        public static XDocument Build(ChapterArchiveRequest req)
        {
            var manga = req?.Manga;
            var chapter = req?.Chapter;
            var release = req?.Release;
            var pageCount = req?.PageCount ?? 0;

            // Build the ordered element list. Null/empty values are dropped by Element().
            // Order matches ComicInfo v2.0 typical reader expectation; readers (Komga / Kavita /
            // Mihon / ComicRack) tolerate any order per the XSD, but we keep this stable so the
            // golden-xml fixture diff is deterministic.
            var elements = new[]
            {
                Element("Title",           chapter?.Title),
                Element("Series",          manga?.Title),

                // Sonarr divergence: Phase 15 Plan 15-10 — manga Chapter.ChapterNumber (decimal)
                // replaces TV EpisodeNumber (int); Chapter.VolumeNumber (int?) replaces SeasonNumber;
                // Chapter.ReleaseDate replaces AirDateUtc.
                Element(
                    "Number",
                    chapter == null ? null : chapter.ChapterNumber.ToString(CultureInfo.InvariantCulture)),
                Element(
                    "Volume",
                    chapter != null && chapter.VolumeNumber.HasValue && chapter.VolumeNumber.Value > 0
                        ? chapter.VolumeNumber.Value.ToString(CultureInfo.InvariantCulture)
                        : null),
                Element("Summary",         manga?.Overview),

                // Phase 16 D-02 — chapter.ReleaseDate -> chapter.FirstReleaseDate.
                // Sonarr-mirror of Episode.AirDateUtc; the upstream chapter-publish date.
                Element("Year",            chapter?.FirstReleaseDate?.Year.ToString(CultureInfo.InvariantCulture)),
                Element("Month",           chapter?.FirstReleaseDate?.Month.ToString(CultureInfo.InvariantCulture)),
                Element("Day",             chapter?.FirstReleaseDate?.Day.ToString(CultureInfo.InvariantCulture)),
                Element(
                    "Genre",
                    manga?.Genres != null && manga.Genres.Count > 0
                        ? string.Join(",", manga.Genres)
                        : null),
                Element("PageCount",       pageCount.ToString(CultureInfo.InvariantCulture)),
                Element("LanguageISO",     release?.TranslatedLanguage),

                // <Web> deferred — Series.cs has no MangaDex ID field yet, will be added in
                // Phase 5 metadata-source work. Series.cs already has MalIds + AniListIds
                // (Phase 2 extension) but neither is the canonical anansi-project <Web> URL
                // for manga; rather than ship a wrong URL or AniList stand-in, omit the
                // element entirely. ComicInfo readers tolerate the absence per v2.0 schema.
                Element("Web",             ResolveMangaWebUrl(manga)),

                // Sonarr divergence: Phase 15 Plan 15-10 — Manga.Certification -> Manga.ContentRating.
                Element("AgeRating",       AgeRatingMapper.Map(manga?.ContentRating)),
                Element("Manga",           "YesAndRightToLeft"),                      // canonical manga reading direction
                Element("ScanInformation", release?.ScanlationGroup),                 // v2.0 — Pitfall 3 fallback (Kavita-safe)
                Element("Translator",      release?.ScanlationGroup),                 // v2.1 — preferred (Komga 1.10+)
                Element(
                    "Tags",
                    manga?.Genres != null && manga.Genres.Count > 0
                        ? string.Join(",", manga.Genres)
                        : null),
            };

            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("ComicInfo", elements));
        }

        // null/empty value → element omitted (clean XML; no <Tag/> placeholders)
        private static XElement Element(string name, string value)
            => string.IsNullOrEmpty(value) ? null : new XElement(name, value);

        // <Web> deferred to Phase 5 metadata-source work; v1 returns null so the element is dropped.
        // Verify-grep guards against accidentally reintroducing an ImdbId-shaped placeholder.
        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — Series -> MangaModel.
        private static string ResolveMangaWebUrl(MangaModel manga) => null;
    }
}
