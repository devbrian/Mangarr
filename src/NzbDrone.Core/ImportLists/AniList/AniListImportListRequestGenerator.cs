using System.Net.Http;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource.AniList;

namespace NzbDrone.Core.ImportLists.AniList
{
    // Phase 27 Plan 27-03 Task 2 — AniList MediaListCollection GraphQL request generator.
    //
    // Pattern source:
    //   27-PATTERNS.md Pattern F (GraphQL transport injection)
    //   27-RESEARCH.md §Example 3 lines 742-787 (MediaListCollection query verbatim)
    //   src/NzbDrone.Core/MetadataSource/AniList/AniListMangaApi.cs (sibling consumer of transport)
    //
    // Transport reuse (Phase 26 Plan 26-02): the SHARED `IAniListGraphQlTransport` (extracted at
    // src/NzbDrone.Core/MetadataSource/AniList/AniListGraphQlTransport.cs) already sets:
    //   * RateLimitKey="anilist" (line 50) — single 30 req/min budget bucket SHARED with
    //     AniListMetadataSource. No per-request RateLimitKey override needed at the ImportList tier.
    //   * Content-Type: application/json header
    //   * HttpMethod.Post + endpoint = https://graphql.anilist.co
    //   * 429-on-rethrow back-pressure logging
    //
    // Wire-format choice: this generator builds a single `HttpRequest` whose Content holds the
    // GraphQL `{ query, variables }` body. AniListImportList overrides
    // `HttpImportListBase.FetchImportListResponse` to detect the GraphQL endpoint and route through
    // `IAniListGraphQlTransport.Post<...>` instead of `_httpClient.Execute(...)` — that override
    // preserves the SourceKey="anilist" budget honoring (transport sets it) while still feeding the
    // substrate's pageable request chain + tier-fallback exception ladder.
    //
    // Anti-injection invariant (T-INJ-03 + Phase 2 AniListMangaApi:23 pattern): the query string is
    // a const string literal; ALL user-supplied values flow through the `variables` object so the
    // GraphQL parser receives them as typed parameters, never as inline interpolation.
    //
    // Pagination: AniList's MediaListCollection is NOT paginated per-list-status — the entire list
    // is returned in a single response. We emit a single chain entry.
    public class AniListImportListRequestGenerator : IImportListRequestGenerator
    {
        // Verbatim copy of the query from RESEARCH §Example 3 lines 752-765. Only fields the
        // parser projects to ImportListItemInfo are requested (id + idMal + title{romaji,english}).
        public const string MediaListCollectionQuery = @"
            query ($userName: String, $status: MediaListStatus) {
              MediaListCollection(userName: $userName, type: MANGA, status: $status) {
                lists {
                  entries {
                    media {
                      id
                      idMal
                      title { romaji english }
                    }
                  }
                }
              }
            }";

        // Phase 1 D-13 — honest UA on every outbound request. Mirrors sibling Plan 27-02
        // MangaDex request generator and the AniList metadata source.
        private static readonly string HonestUserAgent = $"Mangarr/{BuildInfo.Version.ToString(2)}";

        public AniListImportListSettings Settings { get; init; }

        public ImportListPageableRequestChain GetListItems()
        {
            var chain = new ImportListPageableRequestChain();

            // GraphQL body shape: { "query": "...", "variables": { "userName": "...", "status": "CURRENT" } }
            // Use Json.ToJson for the project-default Newtonsoft camelCase contract — this matches
            // sibling AniListMangaApi.BuildBody (Phase 2) verbatim.
            // D-10 single-select propagation: enum → UPPERCASE string matching AniList's
            // MediaListStatus values (CURRENT/PLANNING/COMPLETED/PAUSED/DROPPED/REPEATING).
            var statusVariable = (Settings?.Status ?? AniListListStatus.CURRENT).ToString().ToUpperInvariant();
            var variables = new
            {
                userName = Settings?.AuthUser ?? string.Empty,
                status = statusVariable
            };
            var body = Json.ToJson(new { query = MediaListCollectionQuery, variables });

            // Build the HttpRequest with the GraphQL endpoint + body. AniListImportList's
            // overridden FetchImportListResponse detects this endpoint and routes through
            // IAniListGraphQlTransport.Post; the request's headers / RateLimitKey are NOT applied
            // by the transport path (the transport builds its own internal HttpRequest at
            // AniListGraphQlTransport.cs:47-52), so we set them here only as belt-and-suspenders
            // for any future code path that might run the request through _httpClient.Execute.
            var httpRequest = new HttpRequest(AniListMangaApi.GraphQlEndpoint)
            {
                Method = HttpMethod.Post
            };
            httpRequest.Headers["Content-Type"] = "application/json";
            httpRequest.Headers["User-Agent"] = HonestUserAgent;
            httpRequest.SetContent(body);

            chain.Add(new[] { new ImportListRequest(httpRequest) });
            return chain;
        }
    }
}
