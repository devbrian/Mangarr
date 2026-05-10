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
    // Phase 4 — chapter PAGES response shape for /api/v1/chapters/{id}/pages.
    // The path moved from /api/v2/chapters/{id} to /api/v1/chapters/{id}/pages alongside the
    // overall API-version correction. keiyoushi Dto.kt shape — flat list of image URLs.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-chapter pages response. comix.to returns the image list under
    /// <c>result.images[]</c> (each entry has at least a <c>url</c> field).
    /// </summary>
    public class ComixChapterPagesResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("result")]
        public ComixChapterPagesResult Result { get; set; }

        /// <summary>
        /// Convenience accessor that flattens <c>Result.Images</c> into a plain string list of URLs.
        /// Returns an empty list if the envelope is malformed or empty (no exception path).
        /// </summary>
        [JsonIgnore]
        public List<string> Pages
        {
            get
            {
                var pages = new List<string>();
                if (Result?.Images == null)
                {
                    return pages;
                }

                foreach (var image in Result.Images)
                {
                    if (!string.IsNullOrWhiteSpace(image?.Url))
                    {
                        pages.Add(image.Url);
                    }
                }

                return pages;
            }
        }
    }

    public class ComixChapterPagesResult
    {
        [JsonProperty("images")]
        public List<ComixChapterImage> Images { get; set; }
    }

    public class ComixChapterImage
    {
        [JsonProperty("url")]
        public string Url { get; set; }
    }
}
