using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.Indexers.MangaDex
{
    /// <summary>
    /// HTTP wrapper for MangaDex INDEXER endpoints. Sibling to Phase 2's
    /// <see cref="NzbDrone.Core.MetadataSource.MangaDex.MangaDexApi"/> — both share
    /// <c>SourceKey="mangadex"</c> for unified rate budget (Phase 1 D-11/D-12 +
    /// Phase 2 D-22). Reuses Phase 2's <c>ChapterFeedResource</c> DTO (in
    /// <c>NzbDrone.Core.MetadataSource.MangaDex.Resource</c>) for response
    /// deserialization (Pitfall 7 + Phase 2 D-12 honored downstream by the parser).
    ///
    /// <para>
    /// Phase 8 cleanup: when <c>MetadataSource/MangaDex/</c> collapses with
    /// <c>Indexers/MangaDex/</c>, this class merges with <c>MangaDexApi</c>.
    /// </para>
    ///
    /// <para>
    /// Endpoints used by Phase 3 (per <c>https://api.mangadex.org/docs</c>):
    /// <list type="bullet">
    /// <item><c>GET /chapter</c> — global latest-updates feed (FetchRecent path)</item>
    /// <item><c>GET /manga/{id}/feed</c> — paginated chapter feed for a single manga</item>
    /// </list>
    /// Phase 4's in-process downloader will additionally call <c>GET /at-home/server/{chapterId}</c>
    /// to dereference <see cref="NzbDrone.Core.Parser.Model.ReleaseInfo.DownloadUrl"/> into per-page
    /// image URLs — that endpoint is the 40 req/min throttled hot path and is keyed on the same
    /// <c>SourceKey="mangadex"</c> bucket.
    /// </para>
    /// </summary>
    public class MangaDexIndexerApi
    {
        private const string DefaultBase = "https://api.mangadex.org";

        private readonly IHttpClient _httpClient;
        private readonly Func<string> _userAgent;
        private readonly string _sourceKey;
        private readonly string _baseUrl;

        public MangaDexIndexerApi(IHttpClient httpClient, string baseUrl, Func<string> userAgent, string sourceKey)
        {
            _httpClient = httpClient;
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBase : baseUrl.TrimEnd('/');
            _userAgent = userAgent;
            _sourceKey = sourceKey;
        }

        /// <summary>
        /// Build a chapter-feed URL for a given manga UUID. Used by both
        /// <c>Fetch(MangaSearchCriteria)</c> and <c>Fetch(ChapterSearchCriteria)</c> — the parser
        /// filters by chapter number client-side for the single-chapter case.
        ///
        /// <para>
        /// MangaDex pageSize=500 is the published contract — do not lower without changing
        /// per-call rate-budget math. <c>contentRating[]=safe&amp;contentRating[]=suggestive</c>
        /// excludes erotica/pornographic per default editorial policy (D-Discretion).
        /// </para>
        /// </summary>
        public string BuildFeedUrl(Guid mangaDexId, IReadOnlyList<string> preferredLanguages, int offset = 0)
        {
            var sb = new StringBuilder($"{_baseUrl}/manga/{mangaDexId:D}/feed");
            sb.Append("?limit=500");
            sb.Append($"&offset={offset}");
            sb.Append("&order[chapter]=asc");
            sb.Append("&includes[]=scanlation_group");
            sb.Append("&includes[]=manga");
            sb.Append("&contentRating[]=safe");
            sb.Append("&contentRating[]=suggestive");

            if (preferredLanguages != null && preferredLanguages.Count > 0)
            {
                foreach (var lang in preferredLanguages)
                {
                    if (string.IsNullOrWhiteSpace(lang))
                    {
                        continue;
                    }

                    sb.Append($"&translatedLanguage[]={WebUtility.UrlEncode(lang)}");
                }
            }
            else
            {
                // Safe default; Phase 5 TranslationProfile is authoritative at decision time.
                sb.Append("&translatedLanguage[]=en");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Build a per-chapter point-query URL used by <c>Fetch(ChapterSearchCriteria)</c>.
        /// Hits MangaDex's <c>/chapter</c> endpoint with simultaneous filtering by
        /// <c>manga={UUID}</c> + <c>chapter[]={number}</c> + <c>translatedLanguage[]</c>
        /// — verified against the live OpenAPI spec at
        /// <c>https://api.mangadex.org/docs/static/api.yaml</c>. Returns only releases that
        /// match the requested chapter number, eliminating the whole-feed fan-out.
        ///
        /// <para>
        /// Chapter numbers serialize using <see cref="CultureInfo.InvariantCulture"/> with
        /// the <c>"0.###"</c> format so decimal forms like 12.5 / 1.123 round-trip without
        /// trailing zeros (matches the upstream <c>chapter</c> field format per Phase 2 D-12).
        /// </para>
        /// </summary>
        public string BuildChapterPointQueryUrl(Guid mangaDexId, decimal chapterNumber, IReadOnlyList<string> preferredLanguages)
        {
            var sb = new StringBuilder($"{_baseUrl}/chapter");
            sb.Append($"?manga={mangaDexId:D}");
            sb.Append($"&chapter[]={WebUtility.UrlEncode(chapterNumber.ToString("0.###", CultureInfo.InvariantCulture))}");
            sb.Append("&order[publishAt]=desc");
            sb.Append("&limit=100");
            sb.Append("&includes[]=scanlation_group");
            sb.Append("&includes[]=manga");
            sb.Append("&contentRating[]=safe");
            sb.Append("&contentRating[]=suggestive");

            if (preferredLanguages != null && preferredLanguages.Count > 0)
            {
                foreach (var lang in preferredLanguages)
                {
                    if (string.IsNullOrWhiteSpace(lang))
                    {
                        continue;
                    }

                    sb.Append($"&translatedLanguage[]={WebUtility.UrlEncode(lang)}");
                }
            }
            else
            {
                sb.Append("&translatedLanguage[]=en");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Build the global <c>/chapter</c> latest-updates URL used by FetchRecent().
        /// </summary>
        public string BuildRecentUrl()
            => $"{_baseUrl}/chapter"
             + "?translatedLanguage[]=en"
             + "&order[publishAt]=desc"
             + "&limit=100"
             + "&includes[]=scanlation_group"
             + "&includes[]=manga"
             + "&contentRating[]=safe"
             + "&contentRating[]=suggestive";

        /// <summary>
        /// Apply Phase 1 D-11/D-13 wiring: SourceKey-keyed rate budget + honest UA + JSON Accept.
        /// Used by callers that build their own <see cref="HttpRequest"/>; the Indexer's standard
        /// <c>IIndexer.Fetch</c> path does this implicitly via
        /// <see cref="NzbDrone.Core.Indexers.Http.HttpAggregatorBase{TSettings}.FetchIndexerResponse"/>.
        /// </summary>
        public void ApplyHeaders(HttpRequest req)
        {
            req.RateLimitKey = _sourceKey;
            req.Headers["User-Agent"] = _userAgent();
            req.Headers["Accept"] = "application/json";
        }
    }
}
