using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Http
{
    /// <summary>
    /// Base class for manga aggregator indexers. Extends <see cref="HttpIndexerBase{TSettings}"/>
    /// with three Mangarr-specific behaviors:
    /// <list type="number">
    /// <item>Per-<see cref="SourceKey"/> rate-limit budget shared between indexer pollers and the
    ///       Phase 4 in-process downloader (D-11/D-12).</item>
    /// <item>Honest-by-default <c>Mangarr/{version}</c> User-Agent (D-13).</item>
    /// <item>Per-instance <see cref="IHttpAggregatorSettings.UserAgentOverride"/> opt-out for
    ///       Cloudflare-protected sources (D-14).</item>
    /// </list>
    /// Phase 3 source plugins (MangaDex / comix.to / MangaFire) derive from this base.
    /// </summary>
    public abstract class HttpAggregatorBase<TSettings> : HttpIndexerBase<TSettings>
        where TSettings : IHttpAggregatorSettings, new()
    {
        /// <summary>
        /// Logical source name used as the rate-limit bucket. e.g. "mangadex", "comix.to", "mangafire".
        /// Concrete subclasses MUST override.
        /// </summary>
        public abstract string DefaultSourceKey { get; }

        /// <summary>
        /// Effective <see cref="DefaultSourceKey"/> after applying the user's per-instance override
        /// (D-12). Two indexer instances with the same SourceKey value share one rate budget; divergent
        /// values produce separate budgets per user choice.
        /// </summary>
        protected string SourceKey => string.IsNullOrWhiteSpace(Settings.SourceKey)
            ? DefaultSourceKey
            : Settings.SourceKey;

        /// <summary>
        /// User-overridable rate limit (D-12). Falls back to the base
        /// <see cref="HttpIndexerBase{TSettings}.RateLimit"/> (2 seconds) when the user has not
        /// configured a value.
        /// </summary>
        public override TimeSpan RateLimit => Settings.Rate ?? base.RateLimit;

        protected HttpAggregatorBase(
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger,
            ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, configService, parsingService, logger, localizationService)
        {
        }

        /// <summary>
        /// Honest User-Agent format per D-13: hard-coded <c>"Mangarr/"</c> prefix + assembly version
        /// (major.minor). Does NOT use <see cref="BuildInfo.AppName"/> which still reads "Sonarr"
        /// until Phase 8 rebrand.
        /// </summary>
        protected virtual string BuildUserAgent()
            => $"Mangarr/{BuildInfo.Version.ToString(2)}";

        /// <summary>
        /// Resolve the User-Agent for the next request. Per D-14 the user may override the honest
        /// default per-instance via <see cref="IHttpAggregatorSettings.UserAgentOverride"/>; source
        /// plugins remain responsible for hiding/exposing this field per their ToS posture.
        /// </summary>
        protected string ResolveUserAgent()
            => string.IsNullOrWhiteSpace(Settings.UserAgentOverride)
                ? BuildUserAgent()
                : Settings.UserAgentOverride;

        /// <summary>
        /// Override the base dispatch hook to inject our SourceKey-keyed rate limit and honest UA.
        /// We intentionally do NOT delegate to <c>base.FetchIndexerResponse</c> because that base
        /// implementation re-assigns <c>RateLimitKey = Definition.Id.ToString()</c> as its final
        /// step before dispatch, which would clobber our SourceKey value (D-11/D-12). Instead we
        /// inline the equivalent dispatch logic here so SourceKey survives to <see cref="IHttpClient"/>.
        /// </summary>
        protected override async Task<IndexerResponse> FetchIndexerResponse(IndexerRequest request)
        {
            _logger.Debug("Downloading Feed " + request.HttpRequest.ToString(false));

            // Floor the request rate at our configured RateLimit (preserves base behavior).
            if (request.HttpRequest.RateLimit < RateLimit)
            {
                request.HttpRequest.RateLimit = RateLimit;
            }

            // D-11/D-12: single shared budget per SourceKey value (NOT Definition.Id).
            request.HttpRequest.RateLimitKey = SourceKey;

            // D-13/D-14: honest-by-default UA, opt-out per instance.
            request.HttpRequest.Headers["User-Agent"] = ResolveUserAgent();

            var response = await _httpClient.ExecuteAsync(request.HttpRequest);

            return new IndexerResponse(request, response);
        }

        // ── Phase 3 D-02 — manga overloads ABSTRACT here ─────────────────────────────
        // HttpIndexerBase exposes these as VIRTUAL with the standard SupportsSearch + FetchReleases
        // body. HttpAggregatorBase narrows them to ABSTRACT so concrete manga plugins
        // (MangaDex, Comix) MUST implement them — even though the standard body would suffice,
        // the abstract enforcement makes "manga plugin author forgot to implement Fetch" a
        // compile error rather than a silent no-op.
        public abstract override Task<IList<ReleaseInfo>> Fetch(MangaSearchCriteria searchCriteria);
        public abstract override Task<IList<ReleaseInfo>> Fetch(ChapterSearchCriteria searchCriteria);

        // ── Phase 3 D-14 — per-source HTTP headers for Phase 4 in-process downloader ──
        // Phase 4's <c>InProcessImageDownloadClient</c> consults this hook BEFORE each image
        // GET to apply per-source <c>Referer</c> / <c>Origin</c> headers required by some
        // aggregators (e.g. comix.to / MangaFire). MangaDex returns empty (its
        // <c>at-home/server</c> URLs don't need a Referer); comix.to overrides to return
        // <c>{ "Referer": "https://comix.to/" }</c>. Default empty so plugins only override
        // when needed.
        public virtual Dictionary<string, string> GetDownloadHeaders(ReleaseInfo release)
            => new Dictionary<string, string>();
    }
}
