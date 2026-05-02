using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers
{
    /// <summary>
    /// Wave 0 stub fixture for <see cref="IndexerSourceStatusService"/>. References
    /// NOT-YET-BUILT production types (lands in Plan 03-03 — D-17 per-SourceKey escalation).
    ///
    /// Mirrors the existing <c>IndexerStatusServiceFixture</c> shape (in IndexerTests/) but keys
    /// status on the string SourceKey rather than the int provider Id, so two indexer instances
    /// with the same SourceKey share disable state — the architectural intent of D-17.
    /// </summary>
    [TestFixture]
    public class IndexerSourceStatusServiceFixture : CoreTest<IndexerSourceStatusService>
    {
        private DateTime _epoch;

        [SetUp]
        public void SetUp()
        {
            _epoch = DateTime.UtcNow;

            Mocker.GetMock<IRuntimeInfo>()
                .SetupGet(v => v.StartTime)
                .Returns(_epoch - TimeSpan.FromHours(1));
        }

        private void WithStatus(IndexerSourceStatus status)
        {
            Mocker.GetMock<IIndexerSourceStatusRepository>()
                .Setup(v => v.FindBySourceKey("mangadex"))
                .Returns(status);

            Mocker.GetMock<IIndexerSourceStatusRepository>()
                .Setup(v => v.All())
                .Returns(new[] { status });
        }

        [Test]
        public void should_cancel_backoff_on_success()
        {
            WithStatus(new IndexerSourceStatus
            {
                SourceKey = "mangadex",
                EscalationLevel = 2
            });

            Subject.RecordSuccess("mangadex");

            Mocker.GetMock<IIndexerSourceStatusRepository>()
                .Verify(v => v.Upsert(It.IsAny<IndexerSourceStatus>()));
        }

        [Test]
        public void RecordFailure_increments_escalation()
        {
            WithStatus(new IndexerSourceStatus
            {
                SourceKey = "mangadex",
                EscalationLevel = 0
            });

            Subject.RecordFailure("mangadex");

            Mocker.GetMock<IIndexerSourceStatusRepository>()
                .Verify(v => v.Upsert(It.IsAny<IndexerSourceStatus>()));
        }

        [Test]
        public void GetBlockedSourceKeys_returns_disabled()
        {
            WithStatus(new IndexerSourceStatus
            {
                SourceKey = "mangadex",
                DisabledTill = DateTime.UtcNow.AddHours(1)
            });

            Subject.GetBlockedSourceKeys().Should().HaveCount(1);
        }
    }
}
