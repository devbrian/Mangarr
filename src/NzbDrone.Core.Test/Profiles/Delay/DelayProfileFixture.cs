using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Profiles.Delay;

namespace NzbDrone.Core.Test.Profiles.Delay
{
    // Phase 8 Plan 99-07 — DelayProfile.GetProtocolDelay routes Http to the new HttpDelay column
    // (was UsenetDelay fallback prior to this plan). Adds the regression-floor case for the
    // historical Unknown / unspecified-protocol fallback shape.
    [TestFixture]
    public class DelayProfileFixture
    {
        private DelayProfile BuildProfile() => new DelayProfile
        {
            UsenetDelay = 10,
            TorrentDelay = 20,
            HttpDelay = 30
        };

        [Test]
        public void GetProtocolDelay_returns_UsenetDelay_for_Usenet()
        {
            BuildProfile().GetProtocolDelay(DownloadProtocol.Usenet).Should().Be(10);
        }

        [Test]
        public void GetProtocolDelay_returns_TorrentDelay_for_Torrent()
        {
            BuildProfile().GetProtocolDelay(DownloadProtocol.Torrent).Should().Be(20);
        }

        [Test]
        public void GetProtocolDelay_returns_HttpDelay_for_Http()
        {
            BuildProfile().GetProtocolDelay(DownloadProtocol.Http).Should().Be(30);
        }

        [Test]
        public void GetProtocolDelay_falls_back_to_UsenetDelay_for_Unknown()
        {
            BuildProfile().GetProtocolDelay(DownloadProtocol.Unknown).Should().Be(10);
        }
    }
}
