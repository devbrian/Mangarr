using System.Collections.Generic;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.MangaDex
{
    // Phase 27 Plan 27-02 Task 2 — paginated GET /user/follows/manga request generator.
    //
    // Pattern source (composite):
    //   * src/NzbDrone.Core/Indexers/MangaDex/MangaDexRequestGenerator.cs (paging skeleton)
    //   * 27-RESEARCH.md §Example 5 lines 856-878 (verbatim adaption to ImportList tier)
    //   * 27-PATTERNS.md Pattern E (paginated HTTP — Sonarr-canonical chain shape)
    //
    // Pagination shape: MangaDex /user/follows/manga returns up to 100 items per page
    // (Pitfall 5 — exceeding the page-size cap silently truncates without an error). We
    // walk offset = 0, 100, 200, ... up to MaxNumResultsPerQuery=1000 (HttpImportListBase
    // line 36 constant — break-on-cap is enforced by the base FetchItems loop at
    // HttpImportListBase.cs:88-91). The base's IsFullPage check terminates the walk
    // early when a partial page arrives (offset reached total).
    //
    // Pitfall 10 HARD RULE: every page request sets RateLimitKey="mangadex" — SHARED
    // with MangaDexMetadataSource + MangaDexIndexer + in-process downloader. The single
    // SourceKey budget bucket is the audit gate at Plan 27-05 close-out.
    public class MangaDexImportListRequestGenerator : IImportListRequestGenerator
    {
        // Pitfall 10 — must match MangaDexIndexer.DefaultSourceKey + MangaDexImportListProxy.SharedSourceKey.
        private const string SharedSourceKey = "mangadex";

        // Pitfall 5 — MangaDex /user/follows/manga page size cap (verified against
        // https://api.mangadex.org/docs/02-authentication/personal-clients/).
        private const int PageSize = 100;

        // HttpImportListBase.cs:36 MaxNumResultsPerQuery — base FetchItems loop breaks
        // after pagedReleases.Count >= MaxNumResultsPerQuery. We emit pages up to this
        // cap so the chain is fully populated; the base loop is the actual terminator
        // (IsFullPage early-exit + cap-break — see HttpImportListBase.cs:88-91).
        private const int MaxItems = 1000;

        // Pre-baked User-Agent literal so the request generator stays static-ish (no
        // BuildInfo reflection per request). Phase 1 D-13 honest UA mandatory per
        // MangaDex ToS (Pitfall 4 / SOURCE-07 — verified MangaDexIndexer comments).
        private static readonly string HonestUserAgent = $"Mangarr/{BuildInfo.Version.ToString(2)}";

        public MangaDexImportListSettings Settings { get; init; }

        public ImportListPageableRequestChain GetListItems()
        {
            var chain = new ImportListPageableRequestChain();

            // Phase 31 D-07 (IL2-02) — single chain.Add(IEnumerable) puts all 10 offset-stepped
            // requests into ONE ImportListPageableRequest so the substrate's inner foreach loop
            // at HttpImportListBase.cs:80-97 can break early on IsFullPage()==false (partial page).
            // The 10 separate chain.Add() calls in the prior shape defeated the inner-loop break
            // because each PageableRequest contained only 1 request — the break had nothing to
            // break out of, so all 10 fired regardless of partial-page state.
            //
            // Companion edit: MangaDexImportList.cs ships `public override int PageSize => 100;`
            // so the substrate's IsFullPage default impl (HttpImportListBase.cs:203-206:
            // `PageSize != 0 && page.Count >= PageSize`) evaluates correctly and the inner-loop
            // break at HttpImportListBase.cs:88-96 actually fires on the first partial page.
            var requests = new List<ImportListRequest>();
            for (var offset = 0; offset < MaxItems; offset += PageSize)
            {
                var request = new HttpRequestBuilder("https://api.mangadex.org/user/follows/manga")
                    .AddQueryParam("limit", PageSize)
                    .AddQueryParam("offset", offset)
                    .Build();

                // Pitfall 10 HARD RULE — shared MangaDex bucket. NEVER a sub-bucket;
                // every audit gate (Plan 27-05 SourceKey grep) requires this exact literal.
                request.RateLimitKey = SharedSourceKey;

                // Bearer-token attach — Settings.AccessToken is populated by
                // RequestAction("startOAuth") server-side (D-08). When AccessToken is
                // null/empty (pre-OAuth save), the request will 401 — the base's
                // exception ladder + the provider's RefreshTokenIfNecessary template
                // re-issues credentials and retries.
                request.Headers["Authorization"] = $"Bearer {Settings?.AccessToken}";

                // Phase 1 D-13 — honest UA per MangaDex ToS (Pitfall 4 mitigation).
                request.Headers["User-Agent"] = HonestUserAgent;
                request.Headers["Accept"] = "application/json";

                requests.Add(new ImportListRequest(request));
            }

            chain.Add(requests);

            return chain;
        }
    }
}
