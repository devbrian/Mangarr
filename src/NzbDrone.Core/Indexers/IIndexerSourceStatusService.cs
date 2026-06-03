using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Indexers
{
    /// <summary>
    /// Per-<c>SourceKey</c> escalation API (D-17).
    /// String-keyed sibling to <see cref="IIndexerStatusService"/>. The gateway writes per-source
    /// status via this path; TV indexers (Newznab/Nyaa/Torznab) keep the
    /// per-<c>ProviderId</c> <see cref="IIndexerStatusService"/> path. Phase 8 retires the legacy
    /// path with <c>Tv/</c>.
    ///
    /// Analog: <see cref="IIndexerStatusService"/>.
    /// </summary>
    public interface IIndexerSourceStatusService
    {
        void RecordSuccess(string sourceKey);
        void RecordFailure(string sourceKey, TimeSpan minimumBackOff = default);
        void RecordConnectionFailure(string sourceKey);
        List<IndexerSourceStatus> GetBlockedSourceKeys();
        bool IsBlocked(string sourceKey);
    }

    /// <summary>
    /// Per-SourceKey escalation implementation. Reuses Mangarr's
    /// <see cref="EscalationBackOff.Periods"/> 10-level cadence verbatim (D-18 — 0s/1m/5m/15m/30m/1h/3h/6h/12h/24h);
    /// NO manga-specific tuning. Mirrors the inner shape of
    /// <see cref="ProviderStatusServiceBase{TProvider, TModel}"/> but keys on
    /// <see cref="IndexerSourceStatus.SourceKey"/> (string) instead of ProviderId (int).
    ///
    /// Pitfall 8 mitigation: every escalation transition publishes BOTH
    /// <see cref="IndexerSourceStatusChangedEvent"/> AND a bridge
    /// <see cref="ProviderStatusChangedEvent{IIndexer}"/> so existing health checks listening to the
    /// generic provider event family also re-fire (defense in depth).
    /// </summary>
    public class IndexerSourceStatusService : IIndexerSourceStatusService
    {
        protected readonly object _syncRoot = new object();
        protected readonly IIndexerSourceStatusRepository _repo;
        protected readonly IEventAggregator _eventAggregator;
        protected readonly IRuntimeInfo _runtimeInfo;
        protected readonly Logger _logger;

        // D-18: Mangarr cadence preserved verbatim. EscalationBackOff.Periods = 10 levels.
        protected int MaximumEscalationLevel { get; set; } = EscalationBackOff.Periods.Length - 1;
        protected TimeSpan MinimumTimeSinceInitialFailure { get; set; } = TimeSpan.Zero;
        protected TimeSpan MinimumTimeSinceStartup { get; set; } = TimeSpan.FromMinutes(15);

        public IndexerSourceStatusService(
            IIndexerSourceStatusRepository repo,
            IEventAggregator eventAggregator,
            IRuntimeInfo runtimeInfo,
            Logger logger)
        {
            _repo = repo;
            _eventAggregator = eventAggregator;
            _runtimeInfo = runtimeInfo;
            _logger = logger;
        }

        public virtual List<IndexerSourceStatus> GetBlockedSourceKeys()
        {
            return _repo.All().Where(v => v.IsDisabled()).ToList();
        }

        public virtual bool IsBlocked(string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                return false;
            }

            var status = _repo.FindBySourceKey(sourceKey);
            return status != null && status.IsDisabled();
        }

        protected virtual IndexerSourceStatus GetStatus(string sourceKey)
        {
            return _repo.FindBySourceKey(sourceKey) ?? new IndexerSourceStatus { SourceKey = sourceKey };
        }

        protected virtual TimeSpan CalculateBackOffPeriod(IndexerSourceStatus status)
        {
            var level = Math.Min(MaximumEscalationLevel, status.EscalationLevel);
            return TimeSpan.FromSeconds(EscalationBackOff.Periods[level]);
        }

        public virtual void RecordSuccess(string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                return;
            }

            lock (_syncRoot)
            {
                var status = GetStatus(sourceKey);

                if (status.EscalationLevel == 0)
                {
                    return;
                }

                status.EscalationLevel--;
                status.DisabledTill = null;

                _repo.Upsert(status);

                _eventAggregator.PublishEvent(new IndexerSourceStatusChangedEvent(sourceKey, status));
                _eventAggregator.PublishEvent(new ProviderStatusChangedEvent<IIndexer>(0, ToProviderStatus(status)));
            }
        }

        protected virtual void RecordFailureInner(string sourceKey, TimeSpan minimumBackOff, bool escalate)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                return;
            }

            lock (_syncRoot)
            {
                var status = GetStatus(sourceKey);

                var now = DateTime.UtcNow;
                status.MostRecentFailure = now;

                if (status.EscalationLevel == 0)
                {
                    status.InitialFailure = now;
                    status.EscalationLevel = 1;
                    escalate = false;
                }

                var inStartupGracePeriod = (_runtimeInfo.StartTime + MinimumTimeSinceStartup) > now;
                var inGracePeriod = (status.InitialFailure!.Value + MinimumTimeSinceInitialFailure) > now;

                if (escalate && !inGracePeriod && !inStartupGracePeriod)
                {
                    status.EscalationLevel = Math.Min(MaximumEscalationLevel, status.EscalationLevel + 1);
                }

                if (minimumBackOff != TimeSpan.Zero)
                {
                    while (status.EscalationLevel < MaximumEscalationLevel
                           && CalculateBackOffPeriod(status) < minimumBackOff)
                    {
                        status.EscalationLevel++;
                    }
                }

                if (!inGracePeriod || minimumBackOff != TimeSpan.Zero)
                {
                    status.DisabledTill = now + CalculateBackOffPeriod(status);
                }

                if (inStartupGracePeriod && minimumBackOff == TimeSpan.Zero && status.DisabledTill.HasValue)
                {
                    var maximumDisabledTill = now + TimeSpan.FromSeconds(EscalationBackOff.Periods[2]);
                    if (maximumDisabledTill < status.DisabledTill)
                    {
                        status.DisabledTill = maximumDisabledTill;
                    }
                }

                _repo.Upsert(status);

                _eventAggregator.PublishEvent(new IndexerSourceStatusChangedEvent(sourceKey, status));
                _eventAggregator.PublishEvent(new ProviderStatusChangedEvent<IIndexer>(0, ToProviderStatus(status)));
            }
        }

        public virtual void RecordFailure(string sourceKey, TimeSpan minimumBackOff = default)
        {
            RecordFailureInner(sourceKey, minimumBackOff, escalate: true);
        }

        public virtual void RecordConnectionFailure(string sourceKey)
        {
            RecordFailureInner(sourceKey, TimeSpan.Zero, escalate: false);
        }

        // Bridge SourceKey-shaped status to the int-shaped ProviderStatusBase event payload.
        // ProviderId is 0 (sentinel — meaning "all instances of this SourceKey"); existing
        // IndexerStatusCheck filters by ProviderId join, so the 0 sentinel is intentionally
        // non-matching (defense in depth: IndexerSourceFailureCheck listens via the
        // SourceKey-typed event for authoritative state).
        private static IndexerStatus ToProviderStatus(IndexerSourceStatus s)
        {
            return new IndexerStatus
            {
                ProviderId = 0,
                InitialFailure = s.InitialFailure,
                MostRecentFailure = s.MostRecentFailure,
                EscalationLevel = s.EscalationLevel,
                DisabledTill = s.DisabledTill,
                LastRssSyncReleaseInfo = s.LastRssSyncReleaseInfo
            };
        }
    }
}
