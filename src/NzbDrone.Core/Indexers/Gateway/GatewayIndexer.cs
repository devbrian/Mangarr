using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Indexers.Gateway
{
    /// <summary>
    /// External manga-gateway indexer (Phase 37). Wires the Plan 01-03 pieces into one provider:
    /// the cached <see cref="IGatewayCapabilitiesProvider"/> (/caps), the
    /// <see cref="GatewayRequestGenerator"/> (POST /search + GET /recent), and the
    /// <see cref="GatewayParser"/> (release mapping + per-source warning escalation).
    ///
    /// <para>
    /// Base-class constraint (Pitfall #1 — the near-namesake trap): this extends
    /// <see cref="HttpIndexerBase{TSettings}"/> directly (NOT <c>HttpAggregatorBase</c>);
    /// <see cref="GatewaySettings"/> implements <c>IIndexerSettings</c> (NOT
    /// <c>IHttpAggregatorSettings</c>). There is NO <c>SourceKey</c>, NO <c>GetChapterPages</c>,
    /// NO <c>GetDownloadHeaders</c>, NO <c>IHttpAggregator</c> reference — the gateway is a single
    /// external host with no per-source rate budget on Mangarr's side.
    /// </para>
    ///
    /// <para>
    /// Fetch routes through the kept <see cref="HttpIndexerBase{TSettings}.FetchReleases"/> engine
    /// (inherited concrete <c>Fetch</c>/<c>FetchRecent</c> overrides — NOT re-overridden here) so
    /// Guid-dedup + Indexer identity-stamping (<c>CleanupReleases</c>) run unchanged (GWIX-03).
    /// </para>
    ///
    /// <para>
    /// No grab path is wired: <c>downloadHandle</c> is an opaque token submitted BACK to the
    /// gateway in Phase 38 — never dereferenced here (Pitfall 5 / SSRF). <c>GetDownloadRequest</c>
    /// is inherited and vestigial until then.
    /// </para>
    /// </summary>
    public class GatewayIndexer : HttpIndexerBase<GatewaySettings>
    {
        private readonly IGatewayCapabilitiesProvider _capsProvider;
        private readonly IIndexerSourceStatusService _sourceStatusService;

        public override string Name => "Mangarr Gateway";
        public override DownloadProtocol Protocol => DownloadProtocol.Http;

        public GatewayIndexer(
            IGatewayCapabilitiesProvider capsProvider,
            IIndexerSourceStatusService sourceStatusService,
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IMangaParsingService parsingService,
            Logger logger,
            ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, configService, parsingService, logger, localizationService)
        {
            _capsProvider = capsProvider;
            _sourceStatusService = sourceStatusService;
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
            => new GatewayRequestGenerator
            {
                Settings = Settings,

                // Background read uses the 12h cache (NOT forceRefresh) for the conditional-query
                // SupportedSearchParams filter; the Test() + dropdown bypass the cache (D-01).
                Capabilities = _capsProvider.GetCapabilities(Settings)
            };

        // The parser needs the source-status service for warnings[] → RecordFailure (D-03a);
        // it does NOT come "for free" from a base class (HttpIndexerBase has no SourceKey path).
        public override IParseIndexerResponse GetParser()
            => new GatewayParser(_sourceStatusService, _logger);

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "gatewaySources")
            {
                // GWIX-02 / D-01: the source dropdown ALWAYS refetches a live /caps (cache bypass)
                // so the user sees the gateway's current source list, not a 12h-stale snapshot.
                var caps = _capsProvider.GetCapabilities(Settings, forceRefresh: true);

                // CR-01: caps.Sources can be null when the remote wire JSON is `"sources": null`
                // (Newtonsoft overwrites the field initializer for an explicit-null key). Guard with
                // `?? new List<>()` exactly as Test() does (line ~121) — RequestAction was the outlier.
                var sources = caps.Sources ?? new List<GatewaySourceCap>();

                return new
                {
                    options = sources.Select(s => new { value = s.Key, name = s.Name }).ToList()
                };
            }

            return base.RequestAction(action, query);
        }

        // GWIX-01 / D-04. Do NOT call base/TestConnection — HttpIndexerBase.TestConnection fires a
        // real /recent fetch; the gateway gate is a live /caps probe + a zero-searchable-source check.
        protected override Task Test(List<ValidationFailure> failures)
        {
            GatewayCapabilities caps;

            try
            {
                caps = _capsProvider.GetCapabilities(Settings, forceRefresh: true); // D-01 — live probe
            }
            catch (ApiKeyException)
            {
                failures.Add(new ValidationFailure("ApiKey",
                    _localizationService.GetLocalizedString("IndexerValidationInvalidApiKey")));
                return Task.CompletedTask;
            }
            catch (Exception)
            {
                failures.Add(new ValidationFailure(string.Empty,
                    _localizationService.GetLocalizedString("GatewayValidationUnableToConnect")));
                return Task.CompletedTask;
            }

            var capsSources = caps.Sources ?? new List<GatewaySourceCap>();
            var selected = (Settings.EnabledSources ?? Enumerable.Empty<string>()).ToList();

            // Effective = the enabled caps sources, narrowed to the user's selection (empty = all).
            var enabled = capsSources.Where(s => s.Enabled).ToList();
            var effective = selected.Any()
                ? enabled.Where(s => selected.Contains(s.Key)).ToList()
                : enabled;

            // D-04 base HARD-FAIL: the selection resolves to zero enabled sources on the gateway.
            if (!effective.Any())
            {
                failures.Add(new ValidationFailure(string.Empty,
                    _localizationService.GetLocalizedString(
                        "GatewayValidationNoSearchableSources",
                        new Dictionary<string, object> { { "sources", ResolveNames(selected, capsSources) } })));
                return Task.CompletedTask;
            }

            // Per-feature capability gate (CodeRabbit Major): Test() must validate against the SAME
            // capability the request generator uses for each enabled feature, otherwise a config can
            // pass save-time validation yet return an empty chain at runtime. BuildSearchChain filters
            // on SupportsSearch; GetRecentRequests filters on SupportsRecent. So: a search-enabled
            // indexer needs ≥1 search-capable effective source; an RSS-enabled indexer needs ≥1
            // recent-capable effective source.
            var definition = Definition as IndexerDefinition;
            var searchEnabled = definition == null
                || definition.EnableAutomaticSearch
                || definition.EnableInteractiveSearch;
            var rssEnabled = definition != null && definition.EnableRss;

            // The capability failures name the EFFECTIVE (resolved, enabled) sources — which are
            // non-empty here — NOT the raw selection. With an empty selection (= "all enabled"),
            // ResolveNames would say "(none enabled)", which is misleading when sources ARE enabled
            // and merely lack the capability (CodeRabbit minor follow-up).
            var effectiveNames = string.Join(", ", effective.Select(s => s.Name ?? s.Key));

            if (searchEnabled && !effective.Any(s => s.SupportsSearch))
            {
                failures.Add(new ValidationFailure(string.Empty,
                    _localizationService.GetLocalizedString(
                        "GatewayValidationNoSearchableSources",
                        new Dictionary<string, object> { { "sources", effectiveNames } })));
            }

            if (rssEnabled && !effective.Any(s => s.SupportsRecent))
            {
                failures.Add(new ValidationFailure(string.Empty,
                    _localizationService.GetLocalizedString(
                        "GatewayValidationNoRecentSources",
                        new Dictionary<string, object> { { "sources", effectiveNames } })));
            }

            return Task.CompletedTask;
        }

        // Resolve selected source keys to their /caps display names for the validation message
        // (CodeRabbit minor): users configure friendly names, not internal keys. Falls back to the
        // raw key for a stale selection no longer present in caps, and to a clear placeholder when
        // the selection is empty (= "all enabled", but nothing qualified).
        private static string ResolveNames(List<string> selected, List<GatewaySourceCap> capsSources)
        {
            if (!selected.Any())
            {
                return "(none enabled on the gateway)";
            }

            return string.Join(", ", selected.Select(key =>
                capsSources.FirstOrDefault(s => s.Key == key)?.Name ?? key));
        }
    }
}
