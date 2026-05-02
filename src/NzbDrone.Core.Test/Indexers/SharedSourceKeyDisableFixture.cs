using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers
{
    /// <summary>
    /// Wave 0 stub fixture realizing D-17 architectural intent: the per-SourceKey escalation
    /// model means two indexer instances with the same SourceKey share disable state — recording
    /// a failure for SourceKey "mangadex" disables all indexers using that key, regardless of
    /// each instance's int provider Id.
    ///
    /// References NOT-YET-BUILT production type <see cref="IndexerSourceStatusService"/> +
    /// <c>IsBlocked(string sourceKey)</c> API (lands in Plan 03-03).
    /// </summary>
    [TestFixture]
    public class SharedSourceKeyDisableFixture : CoreTest<IndexerSourceStatusService>
    {
        [Test]
        public void Two_instances_with_same_SourceKey_share_disable_state()
        {
            // Stubbed: when Subject records failure for "mangadex", IsBlocked("mangadex")
            // returns true regardless of the underlying provider int Id — proves D-17 intent.
            Subject.RecordFailure("mangadex", TimeSpan.FromHours(1));
            Subject.IsBlocked("mangadex").Should().BeTrue();
        }
    }
}
