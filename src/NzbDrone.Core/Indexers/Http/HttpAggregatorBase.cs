using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
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
    public abstract class HttpAggregatorBase<TSettings> : HttpIndexerBase<TSettings>, IHttpAggregator
        where TSettings : IHttpAggregatorSettings, new()
    {
        // Phase 3 D-17 / F-01 fix — per-SourceKey escalation sibling. Owned by the base
        // class (not each subclass) so every HttpAggregatorBase descendant tees failure
        // recording to the per-SourceKey path automatically. Phase 8 collapses with Tv/.
        protected readonly IIndexerSourceStatusService _sourceStatusService;

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
        // Phase 4 D-01 — promoted to public for IHttpAggregator interface contract.
        // ChapterPageFetcher (plan 04-03) reads aggregator.SourceKey via the interface to set
        // request.RateLimitKey on every image GET (Pitfall 1 / F-01 class regression guard).
        public string SourceKey
        {
            get
            {
                // WR-04 (#147): defend against early-construction call where Definition
                // (or Definition.Settings) is not yet wired. Fall back to a fresh default
                // settings instance so SourceKey resolves to DefaultSourceKey rather than
                // throwing NullReferenceException.
                var settings = SettingsOrDefault;
                return string.IsNullOrWhiteSpace(settings.SourceKey)
                    ? DefaultSourceKey
                    : settings.SourceKey;
            }
        }

        /// <summary>
        /// User-overridable rate limit (D-12). Falls back to the base
        /// <see cref="HttpIndexerBase{TSettings}.RateLimit"/> (2 seconds) when the user has not
        /// configured a value.
        /// </summary>
        // WR-04 (#147): null-safe Settings access (Definition may be null pre-wire).
        public override TimeSpan RateLimit => SettingsOrDefault.Rate ?? base.RateLimit;

        protected HttpAggregatorBase(
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IIndexerSourceStatusService sourceStatusService,
            IConfigService configService,
            IMangaParsingService parsingService,
            Logger logger,
            ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, configService, parsingService, logger, localizationService)
        {
            _sourceStatusService = sourceStatusService;
        }

        // Phase 3 D-17 / F-01 fix — tee canonical per-ProviderId recording (the base impl)
        // to the per-SourceKey path. Two indexer instances with the same SourceKey value
        // share disable state because they target the same string key. TV indexers stay
        // on the per-ProviderId path verbatim (they don't extend HttpAggregatorBase).
        protected override void RecordSuccess()
        {
            base.RecordSuccess();
            _sourceStatusService.RecordSuccess(SourceKey);
        }

        protected override void RecordFailure()
        {
            base.RecordFailure();
            _sourceStatusService.RecordFailure(SourceKey);
        }

        protected override void RecordFailure(TimeSpan retryAfter)
        {
            base.RecordFailure(retryAfter);
            _sourceStatusService.RecordFailure(SourceKey, retryAfter);
        }

        protected override void RecordConnectionFailure()
        {
            base.RecordConnectionFailure();
            _sourceStatusService.RecordConnectionFailure(SourceKey);
        }

        /// <summary>
        /// Honest User-Agent format per D-13: hard-coded <c>"Mangarr/"</c> prefix + assembly version
        /// (major.minor). Does NOT use <see cref="BuildInfo.AppName"/> which still reads "Mangarr"
        /// until Phase 8 rebrand.
        /// </summary>
        protected virtual string BuildUserAgent()
            => $"Mangarr/{BuildInfo.Version.ToString(2)}";

        /// <summary>
        /// Resolve the User-Agent for the next request. Per D-14 the user may override the honest
        /// default per-instance via <see cref="IHttpAggregatorSettings.UserAgentOverride"/>; source
        /// plugins remain responsible for hiding/exposing this field per their ToS posture.
        /// </summary>
        // Phase 4 D-01 — promoted to public for IHttpAggregator interface contract.
        public string ResolveUserAgent()
        {
            // WR-04 (#147): null-safe Settings access (Definition may be null pre-wire).
            var settings = SettingsOrDefault;
            return string.IsNullOrWhiteSpace(settings.UserAgentOverride)
                ? BuildUserAgent()
                : settings.UserAgentOverride;
        }

        // WR-04 (#147): null-safe accessor for use by the three callers above
        // (SourceKey, RateLimit, ResolveUserAgent). DI / persistence layers can
        // construct a provider instance before Definition is hydrated; falling
        // back to a fresh TSettings keeps all three properties NRE-free and
        // preserves the original "use default" semantics. The base
        // <see cref="IndexerBase{TSettings}.Settings"/> intentionally throws when
        // Definition is wired but malformed — we only paper over the unwired case.
        protected TSettings SettingsOrDefault
            => Definition?.Settings is TSettings hydrated ? hydrated : new TSettings();

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

        // ── Phase 4 D-01/D-04 — chapter manifest dereference per source ──────────────
        // Each manga indexer plugin MUST implement; Phase 4 InProcessImageDownloadClient
        // resolves the indexer instance via _indexerFactory.Get(release.IndexerId) and
        // calls this hook. Per-source quirks (MangaDex /at-home/server token rotation;
        // comix.to flat URL array) stay encapsulated in the plugin. Downloader stays dumb.
        //
        // ABSTRACT (not virtual) per Phase 3 LEARNINGS pattern "Compile-error-driven
        // additive contract": missing implementation = compile error. Subclasses MAY NOT
        // delegate via base.GetChapterPages(release) (CS0205 — abstract has no body).
        public abstract Task<ChapterManifest> GetChapterPages(ReleaseInfo release);
    }
}
