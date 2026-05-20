using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.AniList.Resource
{
    // Phase 27 Plan 27-03 — DTO for the AniList `MediaListCollection` GraphQL response.
    //
    // GraphQL query (verbatim from Plan 27-03 Task 2 / RESEARCH §Example 3):
    //   query ($userName: String, $status: MediaListStatus) {
    //     MediaListCollection(userName: $userName, type: MANGA, status: $status) {
    //       lists {
    //         entries {
    //           media {
    //             id
    //             idMal
    //             title { romaji english }
    //           }
    //         }
    //       }
    //     }
    //   }
    //
    // Response envelope is wrapped in the shared `AniListGraphQlResponse<T>` from
    // src/NzbDrone.Core/MetadataSource/AniList/Resource/AniListGraphQlResponse.cs — the
    // outer `data` field carries one of these `AniListMediaListResource` instances per
    // request (with `MediaListCollection` populated at the top).
    //
    // Newtonsoft is project-wide configured with CamelCasePropertyNamesContractResolver
    // (NzbDrone.Common/Serializer/Newtonsoft.Json/Json.cs), so the PascalCase property
    // names below map to camelCase JSON fields without explicit [JsonProperty] attrs.
    public class AniListMediaListResource
    {
        public AniListMediaListCollection MediaListCollection { get; set; }
    }

    public class AniListMediaListCollection
    {
        public List<AniListMediaList> Lists { get; set; }
    }

    public class AniListMediaList
    {
        public List<AniListMediaListEntry> Entries { get; set; }
    }

    public class AniListMediaListEntry
    {
        public AniListMediaListMedia Media { get; set; }
    }

    // Minimal projection of AniList's `Media` shape — only the fields the parser uses
    // to populate ImportListItemInfo. Full Media DTO (with synonyms / chapters / staff /
    // tags / etc.) lives at MetadataSource/AniList/Resource/AniListMedia.cs and is consumed
    // by the metadata source; the ImportList tier needs only Title + IDs.
    public class AniListMediaListMedia
    {
        public int Id { get; set; }

        // Cross-source MAL ID — promoted to ImportListItemInfo.MalId when non-null. Enables
        // the cross-source resolver in ImportListSyncService to dedup against MAL-sourced
        // ImportList rows that share the same underlying manga.
        public int? IdMal { get; set; }

        public AniListMediaListTitle Title { get; set; }
    }

    public class AniListMediaListTitle
    {
        // Parser prefers English when present, falls back to Romaji. AniList exposes a richer
        // title block (romaji / english / native / userPreferred / synonyms) per AniListMedia.cs
        // — the ImportList tier only needs the user-facing display titles.
        public string Romaji { get; set; }
        public string English { get; set; }
    }
}
