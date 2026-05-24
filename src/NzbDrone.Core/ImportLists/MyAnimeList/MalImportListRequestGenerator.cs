using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    // Phase 27 Plan 27-04 Task 2 — MAL /v2/users/@me/mangalist request generator.
    //
    // Pattern source (composite):
    //   * Sibling Plan 27-02 MangaDex paginated request gen
    //     (src/NzbDrone.Core/ImportLists/MangaDex/MangaDexImportListRequestGenerator.cs)
    //   * 27-PATTERNS.md Pattern E (paginated HTTP — Sonarr-canonical chain shape)
    //   * RESEARCH §Pitfall 5 (page-size cap — MAL accepts limit=1000)
    //
    // Pagination shape: MAL emits `paging.next` as a full URL with `offset=N` query
    // param on each response. The MalImportList provider class (Task 3) overrides
    // FetchImportListResponse to detect `paging.next` and append follow-up requests
    // to the chain at runtime — this generator builds ONLY the initial request.
    //
    // Rationale for the "initial-request-only" shape (per 27-04-PLAN Task 2 action note):
    //   * MAL pagination requires server-issued cursor URLs we cannot know at chain-build
    //     time (cursors carry implementation-specific opaque tokens, not just incrementing
    //     offsets like MangaDex).
    //   * Building a static chain of offset-stepped URLs would over-fetch when the user's
    //     list is short AND under-fetch when MAL adjusts the page size.
    //   * The substrate-shipped HttpImportListBase.FetchItems loop terminates on a partial
    //     page; combined with the provider's per-response cursor-walk, this yields the
    //     correct cumulative paginated read.
    //
    // Pitfall 10 / SourceKey="myanimelist" NEW bucket: every page request (initial here +
    // follow-ups in the provider's override) sets RateLimitKey="myanimelist". The literal
    // is duplicated between this file and MalImportListProxy.cs by design — every appearance
    // is the same string so the Plan 27-05 close-out audit grep gate has zero false positives.
    public class MalImportListRequestGenerator : IImportListRequestGenerator
    {
        // Pitfall 10 / 27-04 charter — NEW SourceKey bucket per CONTEXT line 36.
        // Must match MalImportListProxy.SharedSourceKey verbatim.
        private const string SharedSourceKey = "myanimelist";

        // MAL /v2/users/@me/mangalist page-size cap — verified against
        // https://myanimelist.net/apiconfig/references/api/v2 (limit=1000 is the
        // maximum the API accepts; values above are clamped server-side).
        private const int PageSize = 1000;

        // Phase 31 IN-01 (GH #258): HonestUserAgent moved to MalConstants.HonestUserAgent
        // (single source-of-truth shared with MalImportList.FetchPage cursor follow-ups
        // and MalImportListProxy.ApplySharedHeaders).
        public MalImportListSettings Settings { get; init; }

        public ImportListPageableRequestChain GetListItems()
        {
            var chain = new ImportListPageableRequestChain();

            // Initial request only — MAL cursor pagination via paging.next is walked
            // by MalImportList.FetchPage (Phase 31 D-08 / IL2-05 — closes GH #223).
            // This generator builds the FIRST request; the FetchPage override walks
            // the cursor chain at runtime.
            //
            // Phase 31 D-10 (IL2-03): statusString sourced from MalListStatusExtensions.ToApiString
            // (reads [EnumMember(Value="...")] on MalListStatus via cached reflection). The
            // duplicated local MapStatus switch helper that previously lived in this
            // file at :86-101 was DELETED — single source-of-truth is now the [EnumMember]
            // attribute strings on MalListStatus.cs:25-41.
            var statusString = (Settings?.Status ?? MalListStatus.Reading).ToApiString();
            var request = new HttpRequestBuilder("https://api.myanimelist.net/v2/users/@me/mangalist")
                .AddQueryParam("status", statusString)
                .AddQueryParam("limit", PageSize)
                .AddQueryParam("offset", 0)
                .AddQueryParam("fields", "list_status,num_chapters")
                .Build();

            // Pitfall 10 HARD RULE — NEW bucket; NEVER a different key in this directory.
            // Plan 27-05 close-out grep gate enforces exclusivity.
            request.RateLimitKey = SharedSourceKey;

            // Bearer-token attach — Settings.AccessToken is populated by
            // RequestAction("getOAuthToken") server-side (D-09). When AccessToken is
            // null/empty (pre-OAuth save), the request will 401 — the provider's
            // 401-retry decorator + RefreshTokenIfNecessary template re-issues
            // credentials and retries.
            request.Headers["Authorization"] = $"Bearer {Settings?.AccessToken}";

            // Phase 1 D-13 — honest UA per MAL ToS. Phase 31 IN-01: shared constant.
            request.Headers["User-Agent"] = MalConstants.HonestUserAgent;
            request.Headers["Accept"] = "application/json";

            chain.Add(new[] { new ImportListRequest(request) });
            return chain;
        }
    }
}
