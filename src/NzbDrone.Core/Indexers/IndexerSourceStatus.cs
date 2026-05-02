using System;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers
{
    /// <summary>
    /// Per-<see cref="Http.HttpAggregatorBase{TSettings}.SourceKey"/> escalation status (Phase 3 D-17).
    /// Two indexer instances with the same <c>SourceKey</c> value (e.g. two MangaDex instances)
    /// share one row → failure pressure on one disables both. TV indexers continue to use the
    /// per-<c>ProviderId</c> <see cref="IndexerStatus"/> path UNTOUCHED.
    ///
    /// Q-2 Option A: own <see cref="ModelBase"/> subclass (NOT extending
    /// <see cref="ThingiProvider.Status.ProviderStatusBase"/>) so Phase 8 cleanup is a clean DROP —
    /// when <c>Tv/</c> deletes, <see cref="IndexerStatus"/> retires and this class becomes the
    /// only status path.
    ///
    /// Analog: <see cref="IndexerStatus"/> (per-ProviderId TV path).
    /// </summary>
    public class IndexerSourceStatus : ModelBase
    {
        /// <summary>Logical source name (e.g. "mangadex", "comix.to"). UNIQUE primary key.</summary>
        public string SourceKey { get; set; }

        /// <summary>UTC timestamp of the first failure in the current escalation cycle.</summary>
        public DateTime? InitialFailure { get; set; }

        /// <summary>UTC timestamp of the most recent failure.</summary>
        public DateTime? MostRecentFailure { get; set; }

        /// <summary>0..9 — index into <see cref="ThingiProvider.Status.EscalationBackOff.Periods"/> (D-18).</summary>
        public int EscalationLevel { get; set; }

        /// <summary>UTC timestamp at which the SourceKey is unblocked. Null = not currently disabled.</summary>
        public DateTime? DisabledTill { get; set; }

        /// <summary>Last release info parsed during RSS sync (used by Phase 6 dedup logic).</summary>
        public ReleaseInfo LastRssSyncReleaseInfo { get; set; }

        public virtual bool IsDisabled() => DisabledTill.HasValue && DisabledTill.Value > DateTime.UtcNow;
    }
}
