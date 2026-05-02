using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Indexers
{
    /// <summary>
    /// Published by <see cref="IndexerSourceStatusService"/> on per-SourceKey escalation transitions
    /// (success after failure, new failure, escalation-level bump). Pitfall 8 mitigation: the
    /// existing <c>IndexerStatusCheck</c> listens to <c>ProviderStatusChangedEvent&lt;IIndexer&gt;</c>
    /// (also published — defense in depth); the new
    /// <see cref="HealthCheck.Checks.IndexerSourceFailureCheck"/> listens to THIS event for
    /// SourceKey-targeted reactions.
    ///
    /// Analog: <see cref="ThingiProvider.Events.ProviderStatusChangedEvent{TProvider}"/>.
    /// </summary>
    public class IndexerSourceStatusChangedEvent : IEvent
    {
        public string SourceKey { get; }
        public IndexerSourceStatus Status { get; }

        public IndexerSourceStatusChangedEvent(string sourceKey, IndexerSourceStatus status)
        {
            SourceKey = sourceKey;
            Status = status;
        }
    }
}
