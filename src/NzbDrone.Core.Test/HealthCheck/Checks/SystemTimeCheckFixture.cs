using Moq;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    // Sonarr divergence: Phase 15 D-21 — SystemTimeCheck rewired to no-op (no Mangarr cloud service);
    // original tests verified server-time-vs-system-time delta behavior. v1 always returns Ok.
    [TestFixture]
    public class SystemTimeCheckFixture : CoreTest<SystemTimeCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                .Returns("Some Warning Message");
        }

        [Test]
        public void should_return_ok_unconditionally_in_v1()
        {
            // v1 ships without cloud time-comparison; check is a no-op.
            Subject.Check().ShouldBeOk();
        }
    }
}
