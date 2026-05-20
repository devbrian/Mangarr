using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.MangaDex.Resource
{
    // Phase 27 Plan 27-02 — DTO for GET /user/follows/manga.
    //
    // Sibling: MetadataSource/MangaDex/Resource/MangaListResource.cs + MangaResource.cs.
    // We keep the DTO minimal — only fields the parser projects to ImportListItemInfo.
    //
    // Newtonsoft is project-wide configured with CamelCasePropertyNamesContractResolver
    // (NzbDrone.Common/Serializer/Newtonsoft.Json/Json.cs), so the PascalCase property
    // names below map to camelCase JSON fields without explicit [JsonProperty] attrs.
    //
    // MangaDex envelope (verified shape — see https://api.mangadex.org/docs/02-authentication/personal-clients/):
    //   {
    //     "result": "ok",
    //     "response": "collection",
    //     "data": [
    //       { "id": "<uuid>", "type": "manga",
    //         "attributes": { "title": { "en": "..." }, "altTitles": [...], "links": {...}, ... },
    //         "relationships": [ ... ]
    //       },
    //       ...
    //     ],
    //     "limit": 100,
    //     "offset": 0,
    //     "total": 240
    //   }
    public class MangaDexFollowsResource
    {
        public string Result { get; set; }
        public string Response { get; set; }
        public List<MangaDexFollowsItem> Data { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
        public int Total { get; set; }
    }

    // Per-follow manga record. Sibling shape to
    // MetadataSource/MangaDex/Resource/MangaResource.cs MangaDataItem. We don't reuse
    // that DTO because Phase 8 cleanup hasn't collapsed MetadataSource/MangaDex/ +
    // Indexers/MangaDex/ yet — copying the minimal shape here keeps ImportList free of
    // a transitive dependency on MetadataSource.MangaDex.Resource until that cleanup.
    public class MangaDexFollowsItem
    {
        public string Id { get; set; }                          // UUID string
        public string Type { get; set; }                        // always "manga"
        public MangaDexFollowsAttributes Attributes { get; set; }
        public List<MangaDexFollowsRelationship> Relationships { get; set; }
    }

    public class MangaDexFollowsAttributes
    {
        // Multi-language localized title — BCP-47 code → text. Parser prefers "en",
        // falls back to first non-empty value.
        public Dictionary<string, string> Title { get; set; }

        // PITFALL 7 (MangaResource.cs:55): links values are STRINGS (not ints).
        // Parser runs int.TryParse(links["al"]) / links["mal"] before persisting
        // to ImportListItemInfo.AniListId / .MalId.
        public Dictionary<string, string> Links { get; set; }

        // PITFALL 7b (MangaResource.cs:63): lastChapter ships as STRING (can be "1.2"
        // for half-chapters). Parser doesn't consume this field at the ImportList tier
        // — Manga aggregate handles last-chapter math downstream.
        public string LastChapter { get; set; }
        public int? Year { get; set; }
        public string Status { get; set; }
        public string ContentRating { get; set; }
    }

    public class MangaDexFollowsRelationship
    {
        public string Id { get; set; }
        public string Type { get; set; }                        // "author", "artist", "cover_art"
    }
}
