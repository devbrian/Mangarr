using NzbDrone.Common.EnvironmentInfo;
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

        // Pre-baked User-Agent literal so the generator stays static-ish (no
        // BuildInfo reflection per request). Phase 1 D-13 honest UA mandatory.
        private static readonly string HonestUserAgent = $"Mangarr/{BuildInfo.Version.ToString(2)}";

        public MalImportListSettings Settings { get; init; }

        public ImportListPageableRequestChain GetListItems()
        {
            var chain = new ImportListPageableRequestChain();

            // Initial request only — MAL cursor pagination via paging.next is
            // NOT walked; the substrate's HttpImportListBase.FetchItems loop
            // terminates on partial pages so lists under PageSize=1000 read
            // correctly. Larger lists are truncated at the first page.
            // Tracked in GH #223 for v1.x.
            var statusString = MapStatusToMalString(Settings?.Status ?? MalListStatus.Reading);
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

            // Phase 1 D-13 — honest UA per MAL ToS.
            request.Headers["User-Agent"] = HonestUserAgent;
            request.Headers["Accept"] = "application/json";

            chain.Add(new[] { new ImportListRequest(request) });
            return chain;
        }

        // MalListStatus → MAL API snake_case string. Mirrors the [EnumMember(Value)]
        // attributes on MalListStatus.cs members. Kept local to the generator (instead
        // of cross-file shared utility) so this class stays self-contained — D-10
        // single-select propagation invariant is enforced at exactly one call site.
        private static string MapStatusToMalString(MalListStatus status)
        {
            return status switch
            {
                MalListStatus.Reading => "reading",
                MalListStatus.PlanToRead => "plan_to_read",
                MalListStatus.Completed => "completed",
                MalListStatus.OnHold => "on_hold",
                MalListStatus.Dropped => "dropped",
                _ => "reading",
            };
        }
    }
}
