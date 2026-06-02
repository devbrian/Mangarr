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

        public override string Name => "Manga Gateway";
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
            => new GatewayParser(_sourceStatusService);

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "gatewaySources")
            {
                // GWIX-02 / D-01: the source dropdown ALWAYS refetches a live /caps (cache bypass)
                // so the user sees the gateway's current source list, not a 12h-stale snapshot.
                var caps = _capsProvider.GetCapabilities(Settings, forceRefresh: true);

                return new
                {
                    options = caps.Sources.Select(s => new { value = s.Key, name = s.Name }).ToList()
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

            // A source is searchable when it is enabled AND advertises search OR recent capability.
            var searchable = (caps.Sources ?? new List<GatewaySourceCap>())
                .Where(s => s.Enabled && (s.SupportsSearch || s.SupportsRecent))
                .ToList();

            var selected = (Settings.EnabledSources ?? Enumerable.Empty<string>()).ToList();

            // Empty selection = all enabled+searchable sources (D-04).
            var effective = selected.Any()
                ? searchable.Where(s => selected.Contains(s.Key)).ToList()
                : searchable;

            if (!effective.Any())
            {
                // D-04 HARD-FAIL (blocks save) with an actionable message NAMING the offending
                // selection so the user can fix the misconfiguration at config time.
                var names = selected.Any()
                    ? string.Join(", ", selected)
                    : "(none enabled on the gateway)";

                failures.Add(new ValidationFailure(string.Empty,
                    _localizationService.GetLocalizedString(
                        "GatewayValidationNoSearchableSources",
                        new Dictionary<string, object> { { "sources", names } })));
            }

            return Task.CompletedTask;
        }
    }
}
