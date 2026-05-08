using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Core.MetadataSource.MangaDex.Resource;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.MangaDex
{
    /// <summary>
    /// JSON response parser for MangaDex's <c>/manga/{id}/feed</c> + <c>/chapter</c> endpoints.
    /// Reuses Phase 2's <see cref="ChapterFeedResource"/> DTO (in
    /// <c>NzbDrone.Core.MetadataSource.MangaDex.Resource</c>) so Phase 8 cleanup (when
    /// <c>MetadataSource/MangaDex/</c> collapses with <c>Indexers/MangaDex/</c>) is purely a
    /// directory move — no DTO churn.
    ///
    /// <para>
    /// Populates two new <see cref="ReleaseInfo"/> fields per Phase 3 D-Q4 / SOURCE-04 (added
    /// in Plan 03-02):
    /// <list type="bullet">
    /// <item><see cref="ReleaseInfo.ScanlationGroup"/> — from
    ///       <c>relationships[type=scanlation_group].attributes.name</c></item>
    /// <item><see cref="ReleaseInfo.TranslatedLanguage"/> — BCP-47 from
    ///       <c>attributes.translatedLanguage</c></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Pitfall 7 + Phase 2 D-12: <c>attributes.chapter</c> is STRINGLY-TYPED (decimal as text);
    /// <see cref="decimal.TryParse(string, NumberStyles, IFormatProvider, out decimal)"/> with
    /// <see cref="CultureInfo.InvariantCulture"/> avoids locale-dependent decimal-comma issues.
    /// Entries that fail parse are silently skipped (T-INJ-02 mitigation: no exception path).
    /// </para>
    ///
    /// <para>
    /// <see cref="ReleaseInfo.DownloadUrl"/> is set to the chapter MANIFEST URL
    /// (<c>https://api.mangadex.org/at-home/server/{chapterId}</c>) — NOT a single image URL.
    /// Phase 4's in-process downloader dereferences this manifest to enumerate per-page image
    /// URLs (T-PHASE-4-MANIFEST-DRIFT mitigated by contract; Pitfall 3 documented).
    /// </para>
    /// </summary>
    public class MangaDexParser : IParseIndexerResponse
    {
        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var releases = new List<ReleaseInfo>();
            var content = indexerResponse?.HttpResponse?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return releases;
            }

            var feed = JsonConvert.DeserializeObject<ChapterFeedResource>(content);
            if (feed?.Data == null)
            {
                return releases;
            }

            foreach (var entry in feed.Data)
            {
                var attrs = entry?.Attributes;
                if (attrs == null || string.IsNullOrWhiteSpace(attrs.Chapter))
                {
                    // Skip entries with no chapter number (some special / external chapters).
                    continue;
                }

                // Pitfall 7 + Phase 2 D-12: chapter is STRING; decimal.TryParse with InvariantCulture.
                // Entries that fail to parse are silently skipped (T-INJ-02: no exception path).
                if (!decimal.TryParse(attrs.Chapter, NumberStyles.Number, CultureInfo.InvariantCulture, out var chapterNum))
                {
                    continue;
                }

                var group = entry.Relationships?
                    .FirstOrDefault(r => r.Type == "scanlation_group")?.Attributes?.Name;

                // Manga relationship returns title as a multilingual dictionary on
                // attributes.title (e.g. {"en": "Title"}). The shared RelationshipAttributes
                // DTO covers author / scanlation_group via .Name and cover_art via .FileName;
                // for type="manga" only .Title is populated, so we read from there. Prefer
                // English; fall back to the first available locale; final fallback "Unknown"
                // matches the prior behavior for the no-includes[]=manga case.
                var mangaRel = entry.Relationships?.FirstOrDefault(r => r.Type == "manga");
                var titleDict = mangaRel?.Attributes?.Title;
                string mangaTitle;
                if (titleDict != null && titleDict.TryGetValue("en", out var enTitle) && !string.IsNullOrWhiteSpace(enTitle))
                {
                    mangaTitle = enTitle;
                }
                else if (titleDict != null && titleDict.Count > 0)
                {
                    mangaTitle = titleDict.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "Unknown";
                }
                else
                {
                    mangaTitle = "Unknown";
                }

                var lang = string.IsNullOrWhiteSpace(attrs.TranslatedLanguage) ? "und" : attrs.TranslatedLanguage;

                var titleSb = $"{mangaTitle} - Chapter {chapterNum.ToString("0.###", CultureInfo.InvariantCulture)} [{lang}]";
                if (!string.IsNullOrWhiteSpace(group))
                {
                    titleSb += $" [{group}]";
                }

                releases.Add(new ReleaseInfo
                {
                    Guid = $"mangadex-{entry.Id}",
                    Title = titleSb,
                    Size = 0,                          // unknown until Phase 4 fetches the manifest
                    DownloadUrl = $"https://api.mangadex.org/at-home/server/{entry.Id}",
                    InfoUrl = $"https://mangadex.org/chapter/{entry.Id}",
                    PublishDate = attrs.PublishAt ?? DateTime.UtcNow,
                    DownloadProtocol = DownloadProtocol.Http,
                    ScanlationGroup = group,                          // Plan 03-02 Q-4 / SOURCE-04
                    TranslatedLanguage = lang                         // Plan 03-02 Q-4 / SOURCE-04
                });
            }

            return releases;
        }
    }
}
