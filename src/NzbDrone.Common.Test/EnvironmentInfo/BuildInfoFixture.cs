using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Test.EnvironmentInfo
{
    [TestFixture]
    public class BuildInfoFixture
    {
        [Test]
        public void should_return_version()
        {
            // Sonarr divergence: Phase 21 D-07 reset MANGARR_MAJOR_VERSION from 10 → 1 for the v1.0.0
            // series. The 5/10 historical values are kept in the allow-list to remain compatible with
            // ad-hoc local builds that haven't picked up the new env, but the canonical post-Phase-21
            // CI build produces 1.x.y.z. Update the bare `1` once the v0.x default never recurs.
            BuildInfo.Version.Major.Should().BeOneOf(1, 5, 10);
        }

        [Test]
        public void should_get_branch()
        {
            BuildInfo.Branch.Should().NotBe("unknown");
            BuildInfo.Branch.Should().NotBeNullOrWhiteSpace();
        }
    }
}
