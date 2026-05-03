using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.Indexers.Comix
{
    // ─────────────────────────────────────────────────────────────────────────────
    // ComixDto — JSON response POCOs for comix.to /api/v2/* endpoints.
    //
    // Shape source: Wave 0 SYNTHESIZED fixtures at
    //   src/NzbDrone.Core.Test/Files/Indexers/Comix/{latest_updates,manga_chapters_*}.json
    // (per SOURCE-PROBE-fixtures.md — keiyoushi Dto.kt verbatim shape; live API capture
    // blocked by Cloudflare-cookie-bypass + RC4 hash token; deferred to Plan 03-06 +
    // Phase 4 SOLVE-01).
    //
    // Envelope shape (BOTH endpoints):
    //   { "status": 200, "result": { "items": [...], "pagination": {...} } }
    //
    // Newtonsoft.Json is forgiving on missing fields; if the live API later diverges,
    // update [JsonProperty] attribute names without contract churn (Plan 03-06 captures
    // any deviations in SOURCE-PROBE-comix.md).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generic response envelope shared by comix.to /api/v2/manga (latest updates / search) and
    /// /api/v2/manga/{hash}/chapters (per-manga chapter list). The polymorphic <typeparamref name="TItem"/>
    /// type avoids two duplicate envelope classes.
    /// </summary>
    public class ComixResponse<TItem>
    {
        [JsonProperty("status")]
        public int Status { get; set; }

        [JsonProperty("result")]
        public ComixResult<TItem> Result { get; set; }
    }

    public class ComixResult<TItem>
    {
        [JsonProperty("items")]
        public List<TItem> Items { get; set; }

        [JsonProperty("pagination")]
        public ComixPagination Pagination { get; set; }
    }

    public class ComixPagination
    {
        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("per_page")]
        public int PerPage { get; set; }

        [JsonProperty("current_page")]
        public int CurrentPage { get; set; }

        [JsonProperty("last_page")]
        public int LastPage { get; set; }
    }

    /// <summary>
    /// Manga-list item — one row in the /api/v2/manga (latest updates / search) response.
    /// </summary>
    public class ComixMangaListItem
    {
        [JsonProperty("manga_id")]
        public int MangaId { get; set; }

        /// <summary>Opaque per-manga slug used as the chapter-list path key (e.g. "1mrl", "0krjl").</summary>
        [JsonProperty("hash_id")]
        public string HashId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("original_language")]
        public string OriginalLanguage { get; set; }

        /// <summary>Numeric per fixture; can be int or decimal — accept double then format.</summary>
        [JsonProperty("latest_chapter")]
        public double? LatestChapter { get; set; }

        /// <summary>Unix epoch (seconds).</summary>
        [JsonProperty("chapter_updated_at")]
        public long? ChapterUpdatedAt { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// Chapter-list item — one row in the /api/v2/manga/{hash}/chapters response.
    /// </summary>
    public class ComixChapter
    {
        [JsonProperty("chapter_id")]
        public long ChapterId { get; set; }

        [JsonProperty("scanlation_group_id")]
        public int ScanlationGroupId { get; set; }

        /// <summary>Numeric per fixture (e.g. 1099.5, 1099.0); decimal is appropriate.</summary>
        [JsonProperty("number")]
        public decimal? Number { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("votes")]
        public int Votes { get; set; }

        /// <summary>Unix epoch (seconds).</summary>
        [JsonProperty("updated_at")]
        public long? UpdatedAt { get; set; }

        /// <summary>Group attribution object; null when the chapter is the official source.</summary>
        [JsonProperty("scanlation_group")]
        public ComixScanlationGroup ScanlationGroup { get; set; }

        /// <summary>1 = official; 0 = scanlation. Mapped from Newtonsoft int.</summary>
        [JsonProperty("is_official")]
        public int IsOfficial { get; set; }
    }

    public class ComixScanlationGroup
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Concrete envelope aliases — preserve plan acceptance shape (ComixMangaListResponse,
    // ComixChapterListResponse) while keeping the generic envelope's type-safety. These
    // are simple subclasses that fix TItem; consumers MAY use either the alias or the
    // generic form; Newtonsoft deserializes both identically.
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
    // Phase 4 plan 04-02 — chapter PAGES response shape for /api/v2/chapters/{id}.
    // Synthesized from keiyoushi/extensions-source/src/en/comix/Comix.kt + Dto.kt
    // (Cloudflare blocks live capture; Phase 3 LEARNINGS — synthesized fixtures
    // contract per SOURCE-PROBE-fixtures.md). Plain flat URL array; no rotation
    // tokens (durable URLs — ExpiresAt = null in the resulting ChapterManifest).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-chapter pages response. comix.to returns a flat array of image URLs
    /// at <c>/api/v2/chapters/{chapterId}</c>.
    /// </summary>
    public class ComixChapterPagesResponse
    {
        [JsonProperty("pages")]
        public List<string> Pages { get; set; } = new();
    }
}
