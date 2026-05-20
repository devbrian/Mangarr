using System.Collections.Generic;
using Newtonsoft.Json;
using NzbDrone.Core.ImportLists.MyAnimeList.Resource;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    // Phase 27 Plan 27-04 Task 2 — projects /v2/users/@me/mangalist JSON response onto
    // ImportListItemInfo. Mirrors sibling Plan 27-02 MangaDex parser + Plan 27-03 AniList
    // parser null-safe pattern (every nesting level guarded against missing fields).
    //
    // Response shape (verified — https://myanimelist.net/apiconfig/references/api/v2#operation/users_user_id_mangalist_get):
    //   {
    //     "data": [
    //       {
    //         "node": { "id": 1, "title": "...", "main_picture": { ... } },
    //         "list_status": { "status": "reading", "score": 8, ... }
    //       },
    //       ...
    //     ],
    //     "paging": { "next": "https://api.myanimelist.net/v2/users/@me/mangalist?offset=1000&..." }
    //   }
    //
    // Cross-IDs: MAL returns `node.id` as a typed int (NOT stringly-typed like MangaDex
    // /user/follows/manga `links.al`/`links.mal`). MalId promoted directly to
    // ImportListItemInfo.MalId; downstream ImportListSyncService cross-source resolver
    // promotes the partial-ID row to a full Manga aggregate via MangaDex/AniList lookups
    // (Phase 26 substrate — no Phase 27 work).
    //
    // Pagination NOTE: the MalImportList provider class (Task 3) walks `paging.next` at
    // runtime in its FetchImportListResponse override. The parser itself does NOT need
    // to surface the cursor — it lives on the wire-level DTO (MalMangaListPaging.Next)
    // and is read directly by the provider before re-parsing the deserialized envelope.
    public class MalImportListParser : IParseImportListResponse
    {
        public IList<ImportListItemInfo> ParseResponse(ImportListResponse importListResponse)
        {
            var items = new List<ImportListItemInfo>();
            var content = importListResponse?.HttpResponse?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return items;
            }

            var envelope = JsonConvert.DeserializeObject<MalMangaListResource>(content);
            if (envelope?.Data == null)
            {
                return items;
            }

            foreach (var entry in envelope.Data)
            {
                var node = entry?.Node;
                if (node == null)
                {
                    continue;
                }

                // ImportListBase.IsValidItem rejects rows lacking BOTH Title AND a
                // cross-source ID; pre-filter here so the base's dedup pass doesn't
                // waste cycles on empty-row noise. Node.Id is non-null per MAL API
                // shape (it's the primary key of the manga record), so checking it
                // against 0 also catches stub/null-pun rows.
                if (string.IsNullOrWhiteSpace(node.Title) && node.Id <= 0)
                {
                    continue;
                }

                var item = new ImportListItemInfo
                {
                    Title = node.Title ?? string.Empty,
                };

                // MAL node.id IS the MalId (typed int, no string parse like MangaDex
                // links). Promote only when > 0 (MAL never emits 0 for valid manga,
                // but defensive bound matches sibling AniList parser's > 0 guard).
                if (node.Id > 0)
                {
                    item.MalId = node.Id;
                }

                items.Add(item);
            }

            return items;
        }
    }
}
