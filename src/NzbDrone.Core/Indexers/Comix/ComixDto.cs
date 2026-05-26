using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.Indexers.Comix
{
    // ─────────────────────────────────────────────────────────────────────────────
    // ComixDto — JSON response POCOs for comix.to /api/v1/* endpoints.
    //
    // Shape source: LIVE API capture 2026-05-08 during comix-indexer-404 debug session
    // (see .planning/debug/comix-indexer-404.md). The earlier Phase 3 plan literal
    // (snake_case `hash_id` / `manga_id` / `latest_chapter` against `/api/v2/...`)
    // targeted a non-existent shape — comix.to actually serves /api/v1/ with camelCase
    // field names. Live envelope:
    //
    //   /api/v1/manga?keyword=...   -> { "status":"ok", "result":{ "items":[ ... ], "lastPage":N } }
    //   /api/v1/manga/{hid}         -> { "status":"ok", "result": { ...detail... } }
    //   /api/v1/manga/{hid}/chapters -> { "status":"ok", "result":{ "items":[ ... ], "lastPage":N } }
    //                                   (Phase 17: dispatched via IComixSigner.ProxyFetchAsync;
    //                                   the signer applies the comix.to anti-bot token in-page.)
    //
    // Newtonsoft.Json is forgiving on missing fields; if the live API later diverges,
    // update [JsonProperty] attribute names without contract churn.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generic response envelope shared by both list endpoints. Note: <c>status</c> is a
    /// STRING ("ok" / "error") — earlier plan literal had it typed as <c>int</c>.
    /// </summary>
    public class ComixResponse<TItem>
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("result")]
        public ComixResult<TItem> Result { get; set; }
    }

    public class ComixResult<TItem>
    {
        [JsonProperty("items")]
        public List<TItem> Items { get; set; }

        [JsonProperty("lastPage")]
        public int? LastPage { get; set; }

        [JsonProperty("currentPage")]
        public int? CurrentPage { get; set; }

        [JsonProperty("perPage")]
        public int? PerPage { get; set; }

        [JsonProperty("total")]
        public int? Total { get; set; }
    }

    /// <summary>
    /// Manga-list item — one row in the /api/v1/manga (latest updates / search) response.
    /// Field names mirror the LIVE API (camelCase: <c>hid</c>, <c>id</c>, <c>latestChapter</c>,
    /// <c>chapterUpdatedAt</c>, <c>originalLanguage</c>).
    /// </summary>
    public class ComixMangaListItem
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        /// <summary>Opaque per-manga hash used as the chapter-list path key (e.g. "mr3m0", "gmyj7").</summary>
        [JsonProperty("hid")]
        public string Hid { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("originalLanguage")]
        public string OriginalLanguage { get; set; }

        /// <summary>Numeric per fixture; can be int or decimal — accept double then format.</summary>
        [JsonProperty("latestChapter")]
        public double? LatestChapter { get; set; }

        /// <summary>
        /// ISO-8601 timestamp string (e.g. "2026-05-05T12:34:56.000000Z"), or null. Accept as string
        /// and let consumers parse — the live shape returns this as a date-time string, not a unix epoch.
        /// </summary>
        [JsonProperty("chapterUpdatedAt")]
        public string ChapterUpdatedAt { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// Single-manga detail response — wraps a single object (NOT an items array) in the result envelope.
    /// </summary>
    public class ComixMangaDetailResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("result")]
        public ComixMangaListItem Result { get; set; }
    }

    /// <summary>
    /// Chapter-list item — one row in the /api/v1/manga/{hid}/chapters response.
    /// Field names mirror the LIVE API + keiyoushi's <c>Dto.kt</c> shape.
    /// </summary>
    public class ComixChapter
    {
        /// <summary>Numeric integer chapter id (e.g. 8999048).</summary>
        [JsonProperty("id")]
        public long Id { get; set; }

        /// <summary>Opaque chapter hash id (e.g. "8999048-chapter-20" prefix).</summary>
        [JsonProperty("hid")]
        public string Hid { get; set; }

        /// <summary>Chapter number (e.g. 4, 1099.5). decimal preserves the half-chapter cases.</summary>
        [JsonProperty("number")]
        public decimal? Number { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        /// <summary>ISO-8601 timestamp string, or null.</summary>
        [JsonProperty("updatedAt")]
        public string UpdatedAt { get; set; }

        /// <summary>ISO-8601 timestamp string, or null. Some chapter rows ship publishedAt instead of updatedAt.</summary>
        [JsonProperty("publishedAt")]
        public string PublishedAt { get; set; }

        /// <summary>Group attribution object; null when the chapter is the official source.</summary>
        [JsonProperty("group")]
        public ComixScanlationGroup Group { get; set; }

        /// <summary>
        /// True = official, False = scanlation. The live API returns this as a JSON boolean
        /// (e.g. <c>"isOfficial":false</c>); the Phase 3 plan literal had it as an int. We
        /// type as <c>bool?</c> to match the live shape — Newtonsoft will deserialize either
        /// boolean or numeric 0/1 to a nullable bool with no extra converter (numeric 1 → true,
        /// 0 → false).
        /// </summary>
        [JsonProperty("isOfficial")]
        public bool? IsOfficial { get; set; }

        /// <summary>BCP-47 language code from the chapter row (live API ships this on per-chapter rows).</summary>
        [JsonProperty("language")]
        public string Language { get; set; }
    }

    public class ComixScanlationGroup
    {
        [JsonProperty("id")]
        public int? Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Concrete envelope aliases — preserve plan acceptance shape (ComixMangaListResponse,
    // ComixChapterListResponse) while keeping the generic envelope's type-safety.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Manga-list envelope alias (latest updates / search responses).</summary>
    public class ComixMangaListResponse : ComixResponse<ComixMangaListItem>
    {
    }

    /// <summary>Chapter-list envelope alias (per-manga chapter list response).</summary>
    public class ComixChapterListResponse : ComixResponse<ComixChapter>
    {
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Phase 4 — chapter pages response shape.
    //
    // Phase 17.2 GAP-17-E (2026-05-10): the legacy shape was
    //   /api/v1/chapters/{id}/pages -> { status, result: { images: [{url:absolute}] } }
    // — a separate pages-list endpoint that comix.to retired alongside response-body
    // encryption + per-deploy signer rotation. The bundle's signer allowlist now rejects
    // all /chapters/{id}/<suffix> shapes.
    //
    // The current shape (per 17.2-PAGES-ENDPOINT-SURVEY.md winner verdict):
    //   /api/v1/chapters/{id} -> { e: <encrypted> } -> decrypts in-page to chapter detail
    //   The .NET caller receives the production decrypt-wrap envelope:
    //     { result: { id, mangaId, number, volume, name, language, ..., pages: { baseUrl, items: [{width, height, url:relative}] } } }
    //   See ComixPlaywrightSigner.EvaluateProxyFetchAsync line ~417 — the in-page IIFE
    //   returns JSON.stringify({result: decoded.data}) on the encrypted-body path.
    //
    // Per-image absolute URLs are composed: `Pages.BaseUrl + Items[i].Url` (e.g.,
    // "https://j24n.wowpic3.store/ii/<keyHex>/" + "01.webp"). The Pages convenience
    // accessor on ComixChapterPagesResponse handles this composition.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-chapter pages response (Phase 17.2 GAP-17-E shape — chapter-detail-rooted,
    /// pages list embedded under <c>result.pages.{baseUrl, items[]}</c>).
    /// </summary>
    public class ComixChapterPagesResponse
    {
        /// <summary>
        /// Phase 17 envelope: production wraps the decrypted chapter-detail under
        /// <c>{result: &lt;decoded.data&gt;}</c> (per ComixPlaywrightSigner.EvaluateProxyFetchAsync
        /// line ~417). On the unencrypted-body path, the raw chapter detail is the root —
        /// callers should fall back to <see cref="RootDetailId"/> + <see cref="RootPages"/>
        /// in that case.
        /// </summary>
        [JsonProperty("result")]
        public ComixChapterDetail Result { get; set; }

        /// <summary>
        /// Phase 17.2 fallback: when JsonConvert.DeserializeObject is given a body whose
        /// root IS the chapter detail (no <c>result</c> wrapper — possible if the in-page
        /// IIFE skips the encrypted-body branch and returns the raw text directly), we
        /// also deserialize fields at the root so the convenience accessor still works.
        /// Callers should not depend on this; the encrypted-body path is the primary route.
        /// </summary>
        [JsonProperty("id")]
        public long? RootDetailId { get; set; }

        /// <summary>Direct root-level pages object (matches the unwrapped-body path).</summary>
        [JsonProperty("pages")]
        public ComixChapterPagesContainer RootPages { get; set; }

        /// <summary>
        /// Convenience accessor that composes per-page absolute URLs from
        /// <c>BaseUrl + Items[].Url</c>. Returns an empty list if the envelope is malformed
        /// or empty (no exception path). Tries the wrapped <c>Result.Pages</c> first then
        /// falls back to root-level <c>RootPages</c>.
        /// </summary>
        [JsonIgnore]
        public List<string> Pages
        {
            get
            {
                var urls = new List<string>();
                var container = Result?.Pages ?? RootPages;
                if (container?.Items == null)
                {
                    return urls;
                }

                var baseUrl = container.BaseUrl ?? string.Empty;
                foreach (var item in container.Items)
                {
                    if (string.IsNullOrWhiteSpace(item?.Url))
                    {
                        continue;
                    }

                    // Item.Url may already be absolute (legacy fallback). Detect by
                    // scheme prefix and skip composition in that case so canned/legacy
                    // responses don't accidentally become "https://base/https://other/...".
                    var composed = (item.Url.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
                                    || item.Url.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
                                   ? item.Url
                                   : ComposeUrl(baseUrl, item.Url);
                    urls.Add(composed);
                }

                return urls;
            }
        }

        // Compose baseUrl + relative segment, normalizing the boundary so we don't
        // double or drop the slash. baseUrl typically ends in '/'; relative typically
        // doesn't start with '/'. This handles both cases without producing '//'.
        private static string ComposeUrl(string baseUrl, string relative)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                return relative;
            }

            var trimmedBase = baseUrl.EndsWith("/", System.StringComparison.Ordinal)
                              ? baseUrl
                              : baseUrl + "/";
            var trimmedRel = relative.StartsWith("/", System.StringComparison.Ordinal)
                             ? relative.Substring(1)
                             : relative;
            return trimmedBase + trimmedRel;
        }
    }

    /// <summary>
    /// Phase 17.2 chapter-detail body (rooted under <c>response.result</c> per the production
    /// in-page IIFE wrap). Carries the <see cref="Pages"/> container as the primary payload;
    /// other fields (id, mangaId, number, etc.) are surfaced for completeness but not all
    /// consumed by ComixIndexer.GetChapterPages today.
    /// </summary>
    public class ComixChapterDetail
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("mangaId")]
        public long? MangaId { get; set; }

        [JsonProperty("number")]
        public decimal? Number { get; set; }

        [JsonProperty("volume")]
        public int? Volume { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("isOfficial")]
        public bool? IsOfficial { get; set; }

        [JsonProperty("group")]
        public ComixScanlationGroup Group { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("pages")]
        public ComixChapterPagesContainer Pages { get; set; }
    }

    /// <summary>
    /// Phase 17.2 pages container — holds the CDN base-URL + per-image relative URLs.
    /// Per-image absolute URL is composed by concatenating <c>BaseUrl + Items[i].Url</c>.
    /// </summary>
    public class ComixChapterPagesContainer
    {
        [JsonProperty("baseUrl")]
        public string BaseUrl { get; set; }

        [JsonProperty("items")]
        public List<ComixChapterImage> Items { get; set; }
    }

    /// <summary>
    /// Phase 17.2 per-page image entry. <c>Url</c> is relative under the new shape (e.g.,
    /// <c>"01.webp"</c>) and absolute under the legacy shape (e.g.,
    /// <c>"https://cdn.comix.to/manga/test/ch1/1.jpg"</c>) — composition is handled by
    /// <see cref="ComixChapterPagesResponse.Pages"/>.
    /// </summary>
    public class ComixChapterImage
    {
        [JsonProperty("width")]
        public int? Width { get; set; }

        [JsonProperty("height")]
        public int? Height { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }
}
