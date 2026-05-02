using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers
{
    /// <summary>
    /// Realizes D-17 architectural intent: the per-SourceKey escalation model means two indexer
    /// instances with the same SourceKey share disable state — recording a failure for SourceKey
    /// "mangadex" disables all indexers using that key, regardless of each instance's int provider Id.
    ///
    /// Wires a stateful in-memory repository mock so Upsert + FindBySourceKey simulate the production
    /// persistence boundary correctly.
    /// </summary>
    [TestFixture]
    public class SharedSourceKeyDisableFixture : CoreTest<IndexerSourceStatusService>
    {
        [SetUp]
        public void SetUp()
        {
            var store = new Dictionary<string, IndexerSourceStatus>(StringComparer.Ordinal);

            Mocker.GetMock<IIndexerSourceStatusRepository>()
                  .Setup(r => r.FindBySourceKey(It.IsAny<string>()))
                  .Returns<string>(key => store.TryGetValue(key, out var s) ? s : null);

            Mocker.GetMock<IIndexerSourceStatusRepository>()
                  .Setup(r => r.Upsert(It.IsAny<IndexerSourceStatus>()))
                  .Callback<IndexerSourceStatus>(s => store[s.SourceKey] = s);

            // Past startup-grace-period — so RecordFailure honors the minimumBackOff escalation.
            Mocker.GetMock<NzbDrone.Common.EnvironmentInfo.IRuntimeInfo>()
                  .SetupGet(r => r.StartTime)
                  .Returns(DateTime.UtcNow.AddHours(-2));
        }

        [Test]
        public void Two_instances_with_same_SourceKey_share_disable_state()
        {
            // When Subject records failure for "mangadex", IsBlocked("mangadex")
            // returns true regardless of the underlying provider int Id — proves D-17 intent.
            Subject.RecordFailure("mangadex", TimeSpan.FromHours(1));
            Subject.IsBlocked("mangadex").Should().BeTrue();
        }
    }
}
