using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Indexers.Cloudflare;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    /// <summary>
    /// Phase 33.2 D-07 — pins the distinct "not configured" vs "unreachable" branches of
    /// <see cref="CloudflareSolverCheck"/>. The two warning messages MUST be textually distinct
    /// (so a user gets an unambiguous remedy) and separate from the generic CF-403 message.
    ///
    /// Analog: IndexerSourceFailureCheckFixture (HealthCheckBase + ILocalizationService stub).
    /// </summary>
    [TestFixture]
    public class CloudflareSolverCheckFixture : CoreTest<CloudflareSolverCheck>
    {
        private const string NotConfiguredKey = "CloudflareSolverNotConfiguredHealthCheckMessage";
        private const string UnreachableKey = "CloudflareSolverUnreachableHealthCheckMessage";

        [SetUp]
        public void SetUp()
        {
            // Echo the key back as the message so the fixture can assert the two branches resolve
            // DISTINCT keys (and therefore distinct localized strings) without coupling to copy.
            Mocker.GetMock<ILocalizationService>()
                .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                .Returns<string>(key => key);
        }

        [Test]
        public void should_warn_not_configured_when_solver_url_empty()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.CloudflareSolverUrl)
                .Returns(string.Empty);

            var result = Subject.Check();

            result.Type.Should().Be(HealthCheckResult.Warning);
            result.Message.Should().Be(NotConfiguredKey);

            // Empty URL short-circuits before the probe — the clearance service is never touched.
            Mocker.GetMock<ICloudflareClearanceService>()
                .Verify(s => s.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public void should_return_ok_when_configured_and_probe_succeeds()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.CloudflareSolverUrl)
                .Returns("http://localhost:8191");

            Mocker.GetMock<ICloudflareClearanceService>()
                .Setup(s => s.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new CloudflareClearance(
                    cfClearanceCookie: "redacted-cookie",
                    userAgent: "UA/1.0",
                    cookieDomain: "comix.to",
                    cookiePath: "/",
                    secure: true,
                    httpOnly: true,
                    expiresAt: System.DateTimeOffset.UtcNow.AddMinutes(20))));

            Subject.Check().Type.Should().Be(HealthCheckResult.Ok);
        }

        [Test]
        public void should_warn_unreachable_when_probe_connection_fails()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.CloudflareSolverUrl)
                .Returns("http://localhost:8191");

            Mocker.GetMock<ICloudflareClearanceService>()
                .Setup(s => s.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CloudflareSolverException("comix.to: connection refused"));

            var result = Subject.Check();

            result.Type.Should().Be(HealthCheckResult.Warning);
            result.Message.Should().Be(UnreachableKey);
        }

        [Test]
        public void not_configured_and_unreachable_messages_are_distinct()
        {
            // Not-configured branch.
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.CloudflareSolverUrl)
                .Returns(string.Empty);
            var notConfigured = Subject.Check().Message;

            // Unreachable branch.
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.CloudflareSolverUrl)
                .Returns("http://localhost:8191");
            Mocker.GetMock<ICloudflareClearanceService>()
                .Setup(s => s.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CloudflareSolverException("comix.to: 502"));
            var unreachable = Subject.Check().Message;

            notConfigured.Should().NotBe(unreachable, "D-07 requires two textually-distinct messages");
        }
    }
}
