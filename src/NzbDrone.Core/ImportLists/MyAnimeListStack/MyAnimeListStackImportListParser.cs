using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.MyAnimeListStack
{
    // Quick task 260608-vf9 — regex projection of a MAL stack page's "add to list" item cards
    // onto ImportListItemInfo. No new NuGet dependency (RESEARCH #1 — pure regex, no
    // AngleSharp/HtmlAgilityPack; the codebase is regex-heavy and the item cards are stable).
    //
    // T-VF9-01 (Tampering) mitigation: extraction is anchored ONLY on the specific add-button
    // shape `ownlist/(manga|anime)/add?selected_(manga|anime)_id=<digits>` — NOT on raw
    // /manga/ or /anime/ links. This avoids the sidebar-recommendation false positive
    // documented in RESEARCH line 40 (an unrelated /anime/.../Steel_Ball_Run link on a manga
    // stack must NOT be counted). Null/blank/malformed HTML yields an empty list, never a throw.
    public class MyAnimeListStackImportListParser : IParseImportListResponse
    {
        // Manga add-button: the MAL manga id is the defensive-guard + MalId signal.
        private static readonly Regex MangaAddButtonRegex =
            new(@"ownlist/manga/add\?selected_manga_id=(\d+)", RegexOptions.Compiled);

        // Anime add-button: the anime-stack guard signal (counted, never imported).
        private static readonly Regex AnimeAddButtonRegex =
            new(@"ownlist/anime/add\?selected_anime_id=(\d+)", RegexOptions.Compiled);

        // Title link on the card: /manga/<id>/<Slug_With_Underscores>. Note the leading
        // `myanimelist\.net/manga/` deliberately does NOT match the add-button URL
        // (`myanimelist.net/ownlist/manga/add`) because of the `ownlist/` segment.
        private static readonly Regex MangaTitleLinkRegex =
            new(@"myanimelist\.net/manga/(\d+)/([A-Za-z0-9_\-]+)", RegexOptions.Compiled);

        public IList<ImportListItemInfo> ParseResponse(ImportListResponse importListResponse)
        {
            var items = new List<ImportListItemInfo>();

            var content = importListResponse?.HttpResponse?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return items;
            }

            // First pass: build a best-effort id -> slug-title map from the card title links.
            var titleMap = new Dictionary<int, string>();
            foreach (Match match in MangaTitleLinkRegex.Matches(content))
            {
                if (int.TryParse(match.Groups[1].Value, out var titleId) && !titleMap.ContainsKey(titleId))
                {
                    // Slug → display title: underscores become spaces (best-effort; the
                    // cross-source resolver refines via MalId lookup downstream).
                    titleMap[titleId] = match.Groups[2].Value.Replace('_', ' ').Trim();
                }
            }

            // Second pass: one ImportListItemInfo per distinct manga add-button.
            var seen = new HashSet<int>();
            foreach (Match match in MangaAddButtonRegex.Matches(content))
            {
                if (!int.TryParse(match.Groups[1].Value, out var malId) || malId <= 0)
                {
                    continue;
                }

                // DistinctBy MalId so duplicate anchors (add-button + data-ga-click-param) collapse.
                if (!seen.Add(malId))
                {
                    continue;
                }

                var title = titleMap.TryGetValue(malId, out var slug) ? slug : string.Empty;

                // MalId > 0 alone passes HttpImportListBase.IsValidItem even with an empty Title —
                // the cross-source resolver in ImportListSyncService promotes MalId-only rows.
                items.Add(new ImportListItemInfo
                {
                    MalId = malId,
                    Title = title
                });
            }

            return items;
        }

        // Anime add-button counter — the defensive-guard signal consumed by the provider's
        // Test() override. Counts ONLY the `ownlist/anime/add?selected_anime_id=` add-buttons
        // (NOT raw /anime/ links), so a manga stack with a sidebar /anime/ recommendation
        // counts ZERO anime (RESEARCH line 40).
        public static int CountAnimeItems(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return 0;
            }

            return AnimeAddButtonRegex.Matches(html).Count;
        }
    }
}
