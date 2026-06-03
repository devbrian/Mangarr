using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    /// <summary>
    /// Health Check for per-<c>SourceKey</c>
    /// disable state (Phase 3 D-17 / SOURCE-05). When a manga aggregator's
    /// <see cref="IndexerSourceStatus.DisabledTill"/> is in the future, surface a Warning in
    /// System → Health so the user knows the source is auto-disabled. Reuses Mangarr's
    /// <see cref="HealthCheckReason.IndexerStatusUnavailable"/> reason code (no new code added —
    /// manga + TV indexer disable both fall under "indexer status unavailable" semantically).
    ///
    /// Listens to BOTH event families (Pitfall 8 mitigation — defense in depth):
    /// <list type="bullet">
    ///   <item><see cref="IndexerSourceStatusChangedEvent"/> — published by <see cref="IndexerSourceStatusService"/> for SourceKey-targeted reactions.</item>
    ///   <item><see cref="ProviderStatusChangedEvent{TProvider}"/> — published as a bridge event by the same service.</item>
    ///   <item><see cref="ProviderUpdatedEvent{TProvider}"/> + <see cref="ProviderDeletedEvent{TProvider}"/> — re-fire on provider lifecycle.</item>
    /// </list>
    ///
    /// Analog: <see cref="IndexerStatusCheck"/> (per-ProviderId TV path; UNTOUCHED).
    /// </summary>
    [CheckOn(typeof(ProviderUpdatedEvent<IIndexer>))]
    [CheckOn(typeof(ProviderDeletedEvent<IIndexer>))]
    [CheckOn(typeof(ProviderStatusChangedEvent<IIndexer>))]
    [CheckOn(typeof(IndexerSourceStatusChangedEvent))]
    public class IndexerSourceFailureCheck : HealthCheckBase
    {
        private readonly IIndexerSourceStatusService _sourceStatusService;

        public IndexerSourceFailureCheck(
            IIndexerSourceStatusService sourceStatusService,
            ILocalizationService localizationService)
            : base(localizationService)
        {
            _sourceStatusService = sourceStatusService;
        }

        public override HealthCheck Check()
        {
            var blocked = _sourceStatusService.GetBlockedSourceKeys();

            if (blocked.Empty())
            {
                return new HealthCheck(GetType());
            }

            return new HealthCheck(
                GetType(),
                HealthCheckResult.Warning,
                HealthCheckReason.IndexerStatusUnavailable,
                _localizationService.GetLocalizedString(
                    "IndexerSourceUnavailableHealthCheckMessage",
                    new Dictionary<string, object>
                    {
                        { "sourceKeys", string.Join(", ", blocked.Select(s => s.SourceKey)) }
                    }),
                "#indexer-sources-are-unavailable-due-to-failures");
        }
    }
}
