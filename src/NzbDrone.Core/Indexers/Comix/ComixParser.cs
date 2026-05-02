using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Comix
{
    /// <summary>
    /// JSON response parser for comix.to <c>/api/v2/...</c> endpoints. Auto-detects whether
    /// the response is a chapter-list (per-manga, with <c>chapter_id</c> rows) or a manga-list
    /// (latest updates / search, with <c>manga_id</c> + <c>hash_id</c> rows) by probing the
    /// first item's keys.
    ///
    /// <para>
    /// Populates two new <see cref="ReleaseInfo"/> fields per Phase 3 D-Q4 / SOURCE-04 (added
    /// in Plan 03-02):
    /// <list type="bullet">
    /// <item><see cref="ReleaseInfo.ScanlationGroup"/> — from <c>scanlation_group.name</c>
    ///       when present (null on official chapters).</item>
    /// <item><see cref="ReleaseInfo.TranslatedLanguage"/> — hard-coded <c>"en"</c> per RESEARCH:
    ///       comix.to is a single-language English-only source; chapter rows do NOT carry a
    ///       language code.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Pitfall 7 + Phase 2 D-12: chapter <c>number</c> is decimal — the synthesized fixtures
    /// use numeric form (e.g. 1099.5, 1096.5) which Newtonsoft maps directly to
    /// <see cref="decimal"/>. Entries with no number are silently skipped (T-INJ-02 mitigation:
    /// no exception path).
    /// </para>
    ///
    /// <para>
    /// <see cref="ReleaseInfo.DownloadUrl"/> is set to the chapter MANIFEST URL
    /// (<c>https://comix.to/api/v2/chapters/{chapterId}</c>) — NOT a single image URL.
    /// Phase 4's in-process downloader dereferences this manifest to enumerate per-page image
    /// URLs (T-PHASE-4-MANIFEST-DRIFT mitigated by contract).
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
            // route on the first item's keys (chapter rows have chapter_id; manga rows have
            // manga_id + hash_id).
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

            if (firstItem["chapter_id"] != null)
            {
                var chapters = JsonConvert.DeserializeObject<ComixChapterListResponse>(content);
                return ParseChapterList(chapters);
            }

            if (firstItem["manga_id"] != null && firstItem["hash_id"] != null)
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
                var publishDate = ch.UpdatedAt.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(ch.UpdatedAt.Value).UtcDateTime
                    : DateTime.UtcNow;

                var group = ch.ScanlationGroup?.Name;

                // comix.to chapter rows do NOT carry the manga title (the chapter-list endpoint
                // is keyed per-manga; the parent manga is implicit). Parser leaves a placeholder;
                // upstream MangaParsingService.Map enriches with the matched Manga's title (Phase 2
                // D-08).
                var titleSb = $"Chapter {chapterNum.ToString("0.###", CultureInfo.InvariantCulture)} [en]";
                if (!string.IsNullOrWhiteSpace(ch.Name))
                {
                    titleSb += $" - {ch.Name}";
                }

                if (!string.IsNullOrWhiteSpace(group))
                {
                    titleSb += $" [{group}]";
                }

                releases.Add(new ReleaseInfo
                {
                    Guid = $"comix-chapter-{ch.ChapterId}",
                    Title = titleSb,
                    Size = 0,
                    DownloadUrl = $"{BaseUrl.TrimEnd('/')}/api/v2/chapters/{ch.ChapterId}",
                    InfoUrl = $"{BaseUrl.TrimEnd('/')}/manga/",
                    PublishDate = publishDate,
                    DownloadProtocol = DownloadProtocol.Http,
                    ScanlationGroup = group,                          // null on official rows (is_official=1)
                    TranslatedLanguage = "en"                         // RESEARCH: single-language English-only source
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
                if (m == null || !m.LatestChapter.HasValue || string.IsNullOrWhiteSpace(m.HashId))
                {
                    continue;
                }

                var latestNum = (decimal)m.LatestChapter.Value;
                var publishDate = m.ChapterUpdatedAt.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(m.ChapterUpdatedAt.Value).UtcDateTime
                    : DateTime.UtcNow;

                releases.Add(new ReleaseInfo
                {
                    Guid = $"comix-manga-{m.MangaId}-ch{latestNum.ToString("0.###", CultureInfo.InvariantCulture)}",
                    Title = $"{m.Title ?? "Unknown"} - Chapter {latestNum.ToString("0.###", CultureInfo.InvariantCulture)} [en]",
                    Size = 0,

                    // For latest-updates flow, DownloadUrl points at the chapter-list endpoint;
                    // Phase 6 will fan out to enumerate per-chapter rows (which the
                    // ParseChapterList branch then handles).
                    DownloadUrl = $"{BaseUrl.TrimEnd('/')}/api/v2/manga/{m.HashId}/chapters?page=1&order=desc",
                    InfoUrl = $"{BaseUrl.TrimEnd('/')}/manga/{m.HashId}",
                    PublishDate = publishDate,
                    DownloadProtocol = DownloadProtocol.Http,
                    ScanlationGroup = null,                           // manga-list rows carry no group info
                    TranslatedLanguage = "en"                         // RESEARCH: single-language English-only source
                });
            }

            return releases;
        }
    }
}
