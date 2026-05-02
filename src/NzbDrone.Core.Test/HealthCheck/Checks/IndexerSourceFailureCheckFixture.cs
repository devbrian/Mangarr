using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    /// <summary>
    /// Wave 0 stub fixture for <see cref="IndexerSourceFailureCheck"/>. References NOT-YET-BUILT
    /// production types (lands in Plan 03-03 — D-17 + SOURCE-05 surface).
    ///
    /// Mirrors the existing <c>IndexerStatusCheckFixture</c> shape but keys on the per-SourceKey
    /// disable list returned by <see cref="IIndexerSourceStatusService.GetBlockedSourceKeys"/>.
    /// </summary>
    [TestFixture]
    public class IndexerSourceFailureCheckFixture : CoreTest<IndexerSourceFailureCheck>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<ILocalizationService>()
                .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Returns("Some Warning Message");

            Mocker.GetMock<ILocalizationService>()
                .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                .Returns("Some Warning Message");
        }

        [Test]
        public void should_return_ok_if_no_blocked_sourcekeys()
        {
            Mocker.GetMock<IIndexerSourceStatusService>()
                .Setup(s => s.GetBlockedSourceKeys())
                .Returns(new List<IndexerSourceStatus>());

            Subject.Check().Type.Should().Be(HealthCheckResult.Ok);
        }

        [Test]
        public void should_return_warning_if_any_sourcekey_blocked()
        {
            Mocker.GetMock<IIndexerSourceStatusService>()
                .Setup(s => s.GetBlockedSourceKeys())
                .Returns(new List<IndexerSourceStatus>
                {
                    new IndexerSourceStatus { SourceKey = "mangadex" }
                });

            Subject.Check().Type.Should().Be(HealthCheckResult.Warning);
        }
    }
}
