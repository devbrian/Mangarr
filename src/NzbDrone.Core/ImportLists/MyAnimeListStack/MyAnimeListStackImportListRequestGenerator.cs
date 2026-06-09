using System.Text.RegularExpressions;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.MyAnimeListStack
{
    // Quick task 260608-vf9 — single-page request to the canonical MAL stack page.
    //
    // RESEARCH decision #4: single-page scrape now (the test stack renders all items on one
    // page with no pager). If a future large stack reveals a ?page=N / load-more pattern,
    // extend then — do NOT over-engineer a pager that has no observed trigger.
    //
    // T-VF9-02 (SSRF) HARD RULE: this generator NEVER fetches the raw user input. It extracts
    // ONLY the numeric stack id via ExtractStackId, then rebuilds the URL against the FIXED host
    // https://myanimelist.net/stacks/{id}. A pasted internal/file/alternate-host URL cannot be
    // coerced into an outbound fetch — the worst a hostile input can do is yield no id (rejected
    // by the validator) or a numeric id that resolves to a benign MAL stack page.
    public class MyAnimeListStackImportListRequestGenerator : IImportListRequestGenerator
    {
        // Dedicated rate-limit bucket (RESEARCH #58 — planner discretion). The stack pages live
        // on the myanimelist.net WEBSITE, a different host/budget than the api.myanimelist.net
        // OAuth import list; sharing the "myanimelist" key would mis-budget the website fetch
        // against the API budget. A dedicated bucket keeps the two budgets honest.
        private const string SourceKey = "myanimelist-stack";

        // Phase 1 D-13 honest UA — mirror MangaDexImportListRequestGenerator's HonestUserAgent.
        private static readonly string HonestUserAgent = $"Mangarr/{BuildInfo.Version.ToString(2)}";

        // Bare all-digits id (e.g. "85344").
        private static readonly Regex BareIdRegex = new(@"^\s*(\d+)\s*$", RegexOptions.Compiled);

        // Numeric stack id inside a myanimelist.net/stacks/<id> URL.
        private static readonly Regex StackUrlIdRegex = new(@"stacks/(\d+)", RegexOptions.Compiled);

        public MyAnimeListStackImportListSettings Settings { get; init; }

        // Shared static id-extractor (T-VF9-02): returns the numeric stack id string, or null on
        // no-match. Consumed by BOTH the Settings validator (shape gate) and GetListItems (URL
        // rebuild) so the SSRF-mitigation contract has a single source of truth.
        public static string ExtractStackId(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var bare = BareIdRegex.Match(input);
            if (bare.Success)
            {
                return bare.Groups[1].Value;
            }

            var url = StackUrlIdRegex.Match(input);
            if (url.Success)
            {
                return url.Groups[1].Value;
            }

            return null;
        }

        public ImportListPageableRequestChain GetListItems()
        {
            var chain = new ImportListPageableRequestChain();

            var stackId = ExtractStackId(Settings?.StackUrl);
            if (stackId == null)
            {
                // Validator already rejects no-id inputs; if we still get here (e.g. Test on an
                // unsaved bad value) emit an empty chain rather than fetch the stacks/ root.
                return chain;
            }

            // SSRF mitigation: rebuild against the FIXED host — never the raw Settings.StackUrl.
            var request = new HttpRequestBuilder($"https://myanimelist.net/stacks/{stackId}").Build();
            request.Headers["User-Agent"] = HonestUserAgent;
            request.Headers["Accept"] = "text/html";
            request.RateLimitKey = SourceKey;

            chain.Add(new[] { new ImportListRequest(request) });

            return chain;
        }
    }
}
