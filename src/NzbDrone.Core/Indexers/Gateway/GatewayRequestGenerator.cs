using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Indexers.Gateway.Responses;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.Gateway
{
    /// <summary>
    /// Composes the gateway's JSON wire requests: a <c>POST /search</c> (manga + chapter criteria)
    /// and a <c>GET /recent</c> feed, fanned out to the user's selected ∩ enabled ∩ searchable
    /// sources. Carries <c>Settings</c> + <c>Capabilities</c> (set by <c>GatewayIndexer</c> in
    /// Plan 04's <c>GetRequestGenerator()</c>); the kept <c>FetchReleases</c> engine drives it and
    /// the parser (Plan 03) consumes the response.
    ///
    /// <para>
    /// D-03 (skip-only): an <c>enabled:false</c> caps source is structurally excluded from the
    /// queried <c>sources[]</c> — it is NEVER queried and NEVER triggers <c>RecordFailure</c>
    /// (there is intentionally NO <c>IIndexerSourceStatusService</c> here; recording per-source
    /// failure on a runtime <c>warnings[]</c> entry is the parser's job in Plan 03).
    /// </para>
    /// <para>
    /// Conditional query (Pitfall 4): only params advertised in <c>Capabilities.SupportedSearchParams</c>
    /// are emitted; <c>query</c> is ALWAYS sent as the safe fallback. Page-1 only
    /// (<c>Limits.DefaultPageSize</c>); multi-page is a named deferred idea. <c>since</c> is NOT sent
    /// on <c>/recent</c> (engine watermark dedup is authoritative; Open Question 1).
    /// </para>
    /// </summary>
    public class GatewayRequestGenerator : IIndexerRequestGenerator
    {
        // Set by GatewayIndexer.GetRequestGenerator() (Plan 04). NO IIndexerSourceStatusService
        // field by design — D-03 skip-only is request-side structural.
        public GatewaySettings Settings { get; set; }
        public GatewayCapabilities Capabilities { get; set; }

        public IndexerPageableRequestChain GetRecentRequests()
        {
            var chain = new IndexerPageableRequestChain();

            // Recent fan-out targets sources that are enabled + recent-capable. Empty selection = all.
            var sources = EffectiveSources(s => s.Enabled && s.SupportsRecent);

            // An all-disabled selection yields an empty chain (don't throw — mirror the MangaDex
            // MangaDexId==null empty-chain idiom; the hard-fail is Plan 04's Test() at config time).
            // WR-06: also short-circuit on an EMPTY (non-null) effective set. EffectiveSources returns
            // an empty list (not null) when the selection is empty AND no caps source is
            // recent-capable; building an unscoped /recent here would make the gateway interpret the
            // missing `sources` param as "all sources" and keep polling a gateway that advertises zero
            // recent-capable sources on every RSS tick (contradicts the D-04 don't-query-when-nothing-
            // -searchable intent for the recent/RSS path).
            if (sources == null || !sources.Any())
            {
                return chain;
            }

            // Materialize once so the !Any() check above and the foreach below share one enumeration.
            var sourceList = sources.ToList();

            var builder = new HttpRequestBuilder(Settings.BaseUrl)
                .Resource("recent")
                .AddQueryParam("limit", EffectiveLimit());

            foreach (var source in sourceList)
            {
                builder.AddQueryParam("sources", source);
            }

            foreach (var language in MappedLanguages())
            {
                builder.AddQueryParam("languages", language);
            }

            // IN-02 (deferred): `since` is intentionally NOT sent — the kept engine watermark dedup is
            // authoritative (Open Question 1). Not implemented here by design.
            var httpRequest = builder.Build();
            httpRequest.Method = HttpMethod.Get;
            httpRequest.Headers["X-Api-Key"] = Settings.ApiKey; // never logged

            chain.Add(new[] { new IndexerRequest(httpRequest) });
            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(MangaSearchCriteria searchCriteria)
            => BuildSearchChain("manga", searchCriteria, chapter: null);

        public IndexerPageableRequestChain GetSearchRequests(ChapterSearchCriteria searchCriteria)
            => BuildSearchChain("chapter", searchCriteria, chapter: searchCriteria?.ChapterNumber);

        private IndexerPageableRequestChain BuildSearchChain(string type, MangaSearchCriteriaBase criteria, decimal? chapter)
        {
            var chain = new IndexerPageableRequestChain();

            var sources = EffectiveSources(s => s.Enabled && s.SupportsSearch);
            if (sources == null)
            {
                // Every selected source is disabled/unsearchable — empty chain, no throw.
                return chain;
            }

            var body = new GatewaySearchRequest
            {
                Type = type,

                // query is ALWAYS sent as the safe fallback (Pitfall 4 / conditional query).
                Query = criteria?.Manga?.Title?.Trim() ?? string.Empty,

                Languages = MappedLanguages().ToList(),
                Sources = sources.ToList(),
                Interactive = criteria?.InteractiveSearch ?? false,
                Limit = EffectiveLimit(),
                Offset = 0
            };

            // Conditional query: emit chapter ONLY when advertised in supportedSearchParams.
            if (chapter.HasValue && SupportsParam("chapter"))
            {
                body.Chapter = chapter;
            }

            // IN-01 (deferred): manga-ID-targeted search (GatewaySearchRequest.Ids) and Volume are
            // intentionally NOT populated yet — searches are query-string-only for Phase 37. The DTO
            // fields exist for the deferred ID-search idea; do not remove them.

            // Empty sources[] means "all enabled+searchable" per the contract — omit it.
            if (body.Sources.Count == 0)
            {
                body.Sources = null;
            }

            // Empty languages omit cleanly (the gateway then filters none).
            if (body.Languages.Count == 0)
            {
                body.Languages = null;
            }

            var url = new HttpRequestBuilder(Settings.BaseUrl).Resource("search").Build().Url.FullUri;
            var request = new IndexerRequest(url, HttpAccept.Json);
            request.HttpRequest.Method = HttpMethod.Post;
            request.HttpRequest.SetContent(body.ToJson());
            request.HttpRequest.Headers.ContentType = "application/json";
            request.HttpRequest.Headers["X-Api-Key"] = Settings.ApiKey; // never logged

            chain.Add(new[] { request });
            return chain;
        }

        // EffectiveSources = Settings.EnabledSources ∩ (caps sources matching the capability predicate).
        // Returns null when a non-empty selection resolves to zero effective sources (caller short-circuits
        // to an empty chain). Returns an empty enumerable when the selection is empty AND no caps source
        // matches (= "all", but nothing to query → still empty → caller emits the "all" request shape).
        private IEnumerable<string> EffectiveSources(System.Func<GatewaySourceCap, bool> capable)
        {
            var capsSources = (Capabilities?.Sources ?? new List<GatewaySourceCap>())
                .Where(capable)
                .Select(s => s.Key)
                .ToList();

            var selected = (Settings?.EnabledSources ?? Enumerable.Empty<string>()).ToList();

            if (selected.Count == 0)
            {
                // Empty selection = all enabled+capable sources (D-04).
                return capsSources;
            }

            var effective = capsSources.Where(selected.Contains).ToList();

            // Every selected source is disabled/incapable — signal short-circuit.
            return effective.Count == 0 ? null : effective;
        }

        private bool SupportsParam(string param)
            => Capabilities?.SupportedSearchParams?.Contains(param) ?? false;

        // Per-search result count: a user-provided Settings.ResultLimit override wins (passed through
        // verbatim — the gateway is authoritative on its own MaxPageSize ceiling, so we do NOT clamp);
        // otherwise the caps-advertised DefaultPageSize, falling back to 50.
        private int EffectiveLimit()
        {
            if (Settings?.ResultLimit is int limit && limit > 0)
            {
                return limit;
            }

            var size = Capabilities?.Limits?.DefaultPageSize ?? 0;
            return size > 0 ? size : 50;
        }

        // Settings.MultiLanguages (int Language ids) → BCP-47 two-letter codes (Open Question 2 —
        // send as-is and let the gateway filter). Unknown ids are dropped silently.
        private IEnumerable<string> MappedLanguages()
        {
            foreach (var id in Settings?.MultiLanguages ?? Enumerable.Empty<int>())
            {
                var language = Language.FindById(id);
                if (language == null || language == Language.Unknown)
                {
                    continue;
                }

                var iso = IsoLanguages.Get(language);
                if (iso != null && !string.IsNullOrWhiteSpace(iso.TwoLetterCode))
                {
                    yield return iso.TwoLetterCode;
                }
            }
        }
    }
}
