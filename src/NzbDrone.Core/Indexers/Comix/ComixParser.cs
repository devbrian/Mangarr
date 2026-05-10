using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Comix
{
    /// <summary>
    /// JSON response parser for comix.to <c>/api/v1/...</c> endpoints. Auto-detects whether
    /// the response is a chapter-list (with <c>number</c> field on items) or a manga-list
    /// (with <c>hid</c> + <c>title</c> on items) by probing the first item's keys.
    ///
    /// <para>
    /// Live shape verified during the comix-indexer-404 debug session (2026-05-08) — the
    /// Phase 3 plan literal targeted snake_case under /api/v2/, but comix.to actually serves
    /// camelCase under /api/v1/.
    /// </para>
    ///
    /// <para>
    /// Populates two new <see cref="ReleaseInfo"/> fields per Phase 3 D-Q4 / SOURCE-04 (added
    /// in Plan 03-02):
    /// <list type="bullet">
    /// <item><see cref="ReleaseInfo.ScanlationGroup"/> — from the chapter row's <c>group.name</c>
    ///       when present (null on official chapters).</item>
    /// <item><see cref="ReleaseInfo.TranslatedLanguage"/> — hard-coded <c>"en"</c> per RESEARCH:
    ///       comix.to is a single-language English-only source.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Pitfall 7 + Phase 2 D-12: chapter <c>number</c> is decimal — Newtonsoft maps the JSON
    /// numeric value directly to <see cref="decimal"/>. Entries with no number are silently
    /// skipped (T-INJ-02 mitigation: no exception path).
    /// </para>
    ///
    /// <para>
    /// <see cref="ReleaseInfo.DownloadUrl"/> is set to the chapter DETAIL URL
    /// (<c>https://comix.to/api/v1/chapters/{chapterId}</c>) — NOT a single image URL.
    /// Phase 4's in-process downloader dereferences this and enumerates per-page image URLs
    /// from the embedded <c>pages.{baseUrl, items[]}</c> shape.
    /// </para>
    ///
    /// <para>
    /// Phase 17.2 GAP-17-E (2026-05-10): the legacy shape was
    /// <c>https://comix.to/api/v1/chapters/{chapterId}/pages</c> — a separate
    /// pages-list endpoint that comix.to retired alongside response-body encryption + per-deploy
    /// signer rotation. The bundle's signer allowlist now rejects all
    /// <c>/chapters/{id}/&lt;suffix&gt;</c> shapes; the chapter-detail body itself embeds
    /// the page list under <c>pages.{baseUrl, items[{width,height,url}]}</c>. See
    /// <c>.planning/phases/17.2-comix-signer-driver-layer-fix/17.2-PAGES-ENDPOINT-SURVEY.md</c>
    /// for the full live-survey evidence + winner verdict.
    /// </para>
    /// </summary>
    public class ComixParser : IParseIndexerResponse
    {
        public string BaseUrl { get; set; } = "https://comix.to";

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var releases = new List<ReleaseInfo>();
            var content = indexerResponse?.HttpResponse?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return releases;
            }

            // Probe envelope shape first via JObject so we can route deserialization without
            // catching exceptions. comix.to wraps both endpoints in {status, result:{items[]}};
            // route on the first item's keys (chapter rows have a numeric `number` and an
            // `id` plus typically a `group`; manga rows have `hid` + `title` + `latestChapter`).
            JObject envelope;
            try
            {
                envelope = JObject.Parse(content);
            }
            catch (JsonException)
            {
                return releases;
            }

            var firstItem = envelope?["result"]?["items"]?[0] as JObject;
            if (firstItem == null)
            {
                return releases;
            }

            // Chapter rows always carry a numeric `number` and an integer `id` representing
            // the chapter id. Manga rows carry `hid` + `title` + `latestChapter`.
            var hasChapterNumber = firstItem["number"] != null && firstItem["number"].Type != JTokenType.Null;
            var hasMangaTitle = firstItem["latestChapter"] != null || (firstItem["title"] != null && firstItem["hid"] != null && firstItem["number"] == null);

            if (hasChapterNumber)
            {
                var chapters = JsonConvert.DeserializeObject<ComixChapterListResponse>(content);
                return ParseChapterList(chapters);
            }

            if (hasMangaTitle)
            {
                var mangaList = JsonConvert.DeserializeObject<ComixMangaListResponse>(content);
                return ParseMangaList(mangaList);
            }

            return releases;
        }

        private IList<ReleaseInfo> ParseChapterList(ComixChapterListResponse response)
        {
            var releases = new List<ReleaseInfo>();
            if (response?.Result?.Items == null)
            {
                return releases;
            }

            foreach (var ch in response.Result.Items)
            {
                if (ch == null || !ch.Number.HasValue)
                {
                    continue;
                }

                var chapterNum = ch.Number.Value;

                // The live API does not ship a real timestamp on chapter rows (only the
                // formatted relative-time string "3d"/"4d"/etc. — useless for ordering).
                // Fall back to UtcNow so PublishDate is non-null; downstream PublishDate
                // ordering for chapter releases is best-effort only — Phase 5 ranking goes
                // by Quality + ScanlationGroup priority, not PublishDate.
                var publishDate = ParseTimestamp(ch.UpdatedAt) ?? ParseTimestamp(ch.PublishedAt) ?? DateTime.UtcNow;

                var group = ch.Group?.Name;
                var lang = !string.IsNullOrWhiteSpace(ch.Language) ? ch.Language : "en";

                // comix.to chapter rows do NOT carry the manga title (the chapter-list endpoint
                // is keyed per-manga; the parent manga is implicit). The indexer's Fetch
                // override (ComixIndexer.EnrichTitlesWithMangaName) prefixes the manga title
                // post-parse — keeping the parser format-pure while ensuring downstream
                // MangaParser.ParseChapterTitle can extract a usable MangaTitle for DB lookup.
                var title = $"Chapter {chapterNum.ToString("0.###", CultureInfo.InvariantCulture)} [{lang}]";
                var subtitle = !string.IsNullOrWhiteSpace(ch.Title) ? ch.Title : ch.Name;
                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    title += $" - {subtitle}";
                }

                if (!string.IsNullOrWhiteSpace(group))
                {
                    title += $" [{group}]";
                }

                releases.Add(new ReleaseInfo
                {
                    Guid = $"comix-chapter-{ch.Id}",
                    Title = title,
                    Size = 0,

                    // Phase 17.2 GAP-17-E (2026-05-10): pages-endpoint shape was
                    // /api/v1/chapters/{ch.Id}/pages — bundle's signer allowlist now
                    // rejects all /chapters/{id}/<suffix> shapes; the bare chapter
                    // detail endpoint /api/v1/chapters/{ch.Id} returns 200 + an
                    // encrypted body that decodes to the chapter detail INCLUDING
                    // an embedded pages list under `pages.{baseUrl, items[]}`. See
                    // .planning/phases/17.2-comix-signer-driver-layer-fix/17.2-PAGES-ENDPOINT-SURVEY.md
                    // for survey evidence + winner verdict. ComixIndexer.GetChapterPages
                    // strips the /api/v1 prefix and delegates to IComixSigner.ProxyFetchAsync;
                    // the resulting body is then deserialized via ComixChapterPagesResponse
                    // (the new chapter-detail-rooted shape, not the legacy pages-list shape).
                    DownloadUrl = $"{BaseUrl.TrimEnd('/')}/api/v1/chapters/{ch.Id}",
                    InfoUrl = $"{BaseUrl.TrimEnd('/')}/",
                    PublishDate = publishDate,
                    DownloadProtocol = DownloadProtocol.Http,
                    ScanlationGroup = group,                          // null on official rows
                    TranslatedLanguage = lang                         // per-chapter language from comix.to
                });
            }

            return releases;
        }

        private IList<ReleaseInfo> ParseMangaList(ComixMangaListResponse response)
        {
            var releases = new List<ReleaseInfo>();
            if (response?.Result?.Items == null)
            {
                return releases;
            }

            foreach (var m in response.Result.Items)
            {
                if (m == null || !m.LatestChapter.HasValue || string.IsNullOrWhiteSpace(m.Hid))
                {
                    continue;
                }

                var latestNum = (decimal)m.LatestChapter.Value;
                var publishDate = ParseTimestamp(m.ChapterUpdatedAt) ?? DateTime.UtcNow;

                releases.Add(new ReleaseInfo
                {
                    Guid = $"comix-manga-{m.Id}-ch{latestNum.ToString("0.###", CultureInfo.InvariantCulture)}",
                    Title = $"{m.Title ?? "Unknown"} - Chapter {latestNum.ToString("0.###", CultureInfo.InvariantCulture)} [en]",
                    Size = 0,

                    // For latest-updates flow, DownloadUrl points at the chapter-list endpoint.
                    DownloadUrl = $"{BaseUrl.TrimEnd('/')}/api/v1/manga/{m.Hid}/chapters",
                    InfoUrl = $"{BaseUrl.TrimEnd('/')}/title/{m.Hid}",
                    PublishDate = publishDate,
                    DownloadProtocol = DownloadProtocol.Http,
                    ScanlationGroup = null,                           // manga-list rows carry no group info
                    TranslatedLanguage = "en"                         // RESEARCH: single-language English-only source
                });
            }

            return releases;
        }

        /// <summary>
        /// Parse comix.to's timestamp shape. The live API returns ISO-8601 strings
        /// ("2026-05-05T12:34:56.000000Z"); some legacy fixtures used integer unix-epoch seconds.
        /// Accept both — return UTC <see cref="DateTime"/> on success, null on failure.
        /// </summary>
        private static DateTime? ParseTimestamp(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
            {
                return dt;
            }

            // Legacy unix-epoch fallback.
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
            {
                return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
            }

            return null;
        }
    }
}
