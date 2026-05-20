using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.ImportLists.MyAnimeList.Resource
{
    // Phase 27 Plan 27-04 Task 1 — DTO for MAL `/v2/users/@me/mangalist` REST response.
    //
    // Endpoint shape (verified — https://myanimelist.net/apiconfig/references/api/v2#operation/users_user_id_mangalist_get):
    //   {
    //     "data": [
    //       {
    //         "node": {
    //           "id": 1,
    //           "title": "...",
    //           "main_picture": { "medium": "...", "large": "..." }
    //         },
    //         "list_status": {
    //           "status": "reading",
    //           "score": 8,
    //           "num_chapters_read": 23,
    //           "is_rereading": false,
    //           "updated_at": "2024-01-01T00:00:00+00:00"
    //         }
    //       },
    //       ...
    //     ],
    //     "paging": {
    //       "next": "https://api.myanimelist.net/v2/users/@me/mangalist?offset=1000&..."
    //     }
    //   }
    //
    // Parser projects each `data[].node` to ImportListItemInfo { Title = node.title, MalId = node.id }.
    // The `list_status` block and `main_picture` block are not currently used — the parser ignores
    // them but they're declared here so future enhancements (e.g., score-based filtering, cover-art
    // pre-population) can promote them without DTO churn.
    //
    // Pagination: MAL returns `paging.next` as a full URL with `offset=N` query param. The parser
    // ALSO walks this cursor; the request generator (Task 2) builds the initial request only.
    // When `paging.next` is null/missing, the walk terminates.
    //
    // Phase-wide JSON convention: project default is camelCase serialization, but MAL emits
    // SNAKE_CASE (e.g., `main_picture`, `list_status`, `num_chapters_read`). We use explicit
    // [JsonProperty] attributes where the MAL field name differs from the C# property name.
    public class MalMangaListResource
    {
        [JsonProperty("data")]
        public List<MalMangaListEntry> Data { get; set; }

        [JsonProperty("paging")]
        public MalMangaListPaging Paging { get; set; }
    }

    public class MalMangaListEntry
    {
        [JsonProperty("node")]
        public MalMangaListNode Node { get; set; }

        [JsonProperty("list_status")]
        public MalMangaListStatus ListStatus { get; set; }
    }

    public class MalMangaListNode
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("main_picture")]
        public MalMangaListPicture MainPicture { get; set; }
    }

    public class MalMangaListPicture
    {
        [JsonProperty("medium")]
        public string Medium { get; set; }

        [JsonProperty("large")]
        public string Large { get; set; }
    }

    // list_status payload — not currently consumed by the parser but declared for
    // shape completeness so future enhancements (score-filter, last-chapter-read sync)
    // can promote these fields without a DTO migration.
    public class MalMangaListStatus
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("score")]
        public int? Score { get; set; }

        [JsonProperty("num_chapters_read")]
        public int? NumChaptersRead { get; set; }

        [JsonProperty("is_rereading")]
        public bool? IsRereading { get; set; }

        [JsonProperty("updated_at")]
        public string UpdatedAt { get; set; }
    }

    public class MalMangaListPaging
    {
        // MAL returns the NEXT-page URL as a full URL (NOT a relative path) — the
        // parser uses it verbatim as the next-request URL. When the field is null or
        // the property is missing entirely, the walk terminates.
        [JsonProperty("next")]
        public string Next { get; set; }
    }
}
