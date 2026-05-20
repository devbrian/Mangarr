using System.Collections.Generic;
using Newtonsoft.Json;
using NzbDrone.Core.ImportLists.AniList.Resource;
using NzbDrone.Core.MetadataSource.AniList.Resource;

namespace NzbDrone.Core.ImportLists.AniList
{
    // Phase 27 Plan 27-03 Task 2 — projects AniList MediaListCollection GraphQL response onto
    // ImportListItemInfo rows.
    //
    // Response shape (RESEARCH §Example 3 + AniListGraphQlResponse<T> wrapper):
    //   {
    //     "data": {
    //       "MediaListCollection": {
    //         "lists": [
    //           {
    //             "entries": [
    //               { "media": { "id": 30002, "idMal": 13, "title": { "romaji": "...", "english": "..." } } },
    //               ...
    //             ]
    //           },
    //           ...
    //         ]
    //       }
    //     }
    //   }
    //
    // Title preference: prefer `title.english`, fall back to `title.romaji` (CONTEXT line 281
    // does not pin a preference; sibling AniListMetadataSource.MapManga picks userPreferred-first
    // because metadata-source has the full title block — the ImportList tier only requests
    // english+romaji to keep the payload minimal). English-first matches the typical
    // English-speaking-user expectation for a Mangarr-rendered import-list preview.
    //
    // ID promotion: AniList returns `media.id` (AniList integer ID, non-null) and `media.idMal`
    // (MAL integer ID, nullable). Both are projected to ImportListItemInfo.AniListId / .MalId
    // verbatim (no string parsing — AniList native ints, no Pitfall 7 to worry about). The
    // cross-source resolver in ImportListSyncService later promotes the partial-ID row to a full
    // Manga aggregate by querying MangaDex with the resolved AniList ID.
    //
    // Sonarr divergence (Phase 26 Plan 26-04): no MangaDexId is available from AniList directly —
    // rows ship with Title + AniListId + (optional) MalId, and the substrate's manga-triplet
    // dedup key handles them per HttpImportListBase.IsValidItem.
    public class AniListImportListParser : IParseImportListResponse
    {
        public IList<ImportListItemInfo> ParseResponse(ImportListResponse importListResponse)
        {
            var items = new List<ImportListItemInfo>();
            var content = importListResponse?.HttpResponse?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return items;
            }

            // The response is wrapped in the shared GraphQL envelope { data: { MediaListCollection: ... } }.
            // We deserialize using the existing AniListGraphQlResponse<T> generic from the metadata-source
            // sibling — no duplicate wrapper at the ImportList tier (Pitfall: parallel-DTO drift).
            var envelope = JsonConvert.DeserializeObject<AniListGraphQlResponse<AniListMediaListResource>>(content);
            var collection = envelope?.Data?.MediaListCollection;
            if (collection?.Lists == null)
            {
                return items;
            }

            foreach (var list in collection.Lists)
            {
                if (list?.Entries == null)
                {
                    continue;
                }

                foreach (var entry in list.Entries)
                {
                    var media = entry?.Media;
                    if (media == null)
                    {
                        continue;
                    }

                    // Title preference: English first, fall back to Romaji. Empty/null titles
                    // yield ImportListBase.IsValidItem rejection downstream unless the AniListId
                    // carries the row (it always does because AniList Media.id is non-null).
                    var title = !string.IsNullOrWhiteSpace(media.Title?.English)
                        ? media.Title.English
                        : media.Title?.Romaji ?? string.Empty;

                    var item = new ImportListItemInfo
                    {
                        Title = title,
                        AniListId = media.Id,
                    };

                    // MalId is nullable per AniListMediaListMedia.IdMal type — promote only when
                    // non-null AND positive (Pitfall: AniList sometimes ships 0 for MAL-untracked
                    // manga; the > 0 guard matches sibling MangaDex parser's pattern).
                    if (media.IdMal.HasValue && media.IdMal.Value > 0)
                    {
                        item.MalId = media.IdMal.Value;
                    }

                    items.Add(item);
                }
            }

            return items;
        }
    }
}
