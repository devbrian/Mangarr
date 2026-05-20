using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Core.ImportLists.MangaDex.Resource;

namespace NzbDrone.Core.ImportLists.MangaDex
{
    // Phase 27 Plan 27-02 Task 2 — projects /user/follows/manga JSON response onto
    // ImportListItemInfo. Mirrors Indexers/MangaDex/MangaDexParser.cs's null-safe pattern
    // (Pitfall 7 / T-INJ-02 mitigation — every cross-source-ID field is STRINGLY-TYPED
    // by MangaDex and requires int.TryParse before promotion to int? columns).
    public class MangaDexImportListParser : IParseImportListResponse
    {
        public IList<ImportListItemInfo> ParseResponse(ImportListResponse importListResponse)
        {
            var items = new List<ImportListItemInfo>();
            var content = importListResponse?.HttpResponse?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return items;
            }

            var envelope = JsonConvert.DeserializeObject<MangaDexFollowsResource>(content);
            if (envelope?.Data == null)
            {
                return items;
            }

            foreach (var follow in envelope.Data)
            {
                if (follow?.Attributes == null)
                {
                    continue;
                }

                // Multi-language title selection: prefer "en", fall back to the first
                // non-empty value. MangaDex's localized title block is ALWAYS a Dictionary;
                // it's never null for an active manga (verified against
                // /api/v5/manga/lookup payloads + sibling MetadataSource parser).
                var title = ResolveLocalizedTitle(follow.Attributes.Title);
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(follow.Id))
                {
                    // ImportListBase.IsValidItem rejects rows lacking BOTH Title AND
                    // MangaDexId; pre-filter here so the base's dedup pass doesn't waste
                    // cycles on empty-row noise.
                    continue;
                }

                var item = new ImportListItemInfo
                {
                    Title = title ?? string.Empty,
                    MangaDexId = follow.Id,
                };

                // Pitfall 7 (MangaResource.cs:55 comment): links values are STRINGS
                // (not ints). int.TryParse before promotion to ImportListItemInfo.AniListId
                // / .MalId — failure is silent (keep MangaDexId-only row).
                if (follow.Attributes.Links != null)
                {
                    if (follow.Attributes.Links.TryGetValue("al", out var anilistRaw)
                        && int.TryParse(anilistRaw, out var anilistId)
                        && anilistId > 0)
                    {
                        item.AniListId = anilistId;
                    }

                    if (follow.Attributes.Links.TryGetValue("mal", out var malRaw)
                        && int.TryParse(malRaw, out var malId)
                        && malId > 0)
                    {
                        item.MalId = malId;
                    }
                }

                // Year-derived ReleaseDate (best-effort — MangaDex doesn't expose
                // a "follow added at" timestamp on /user/follows/manga). Fall back
                // to DateTime.MinValue when year is null; downstream consumers handle
                // the sentinel.
                if (follow.Attributes.Year.HasValue)
                {
                    item.ReleaseDate = new DateTime(follow.Attributes.Year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                }

                items.Add(item);
            }

            return items;
        }

        // Multi-language title resolution: prefer "en", then first non-empty value.
        // MangaDex stores titles as Dictionary<string,string> (BCP-47 code -> localized
        // string) per MangaAttributes.Title (MangaResource.cs:49).
        private static string ResolveLocalizedTitle(Dictionary<string, string> titleBlock)
        {
            if (titleBlock == null || titleBlock.Count == 0)
            {
                return null;
            }

            if (titleBlock.TryGetValue("en", out var enTitle) && !string.IsNullOrWhiteSpace(enTitle))
            {
                return enTitle;
            }

            return titleBlock.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }
    }
}
