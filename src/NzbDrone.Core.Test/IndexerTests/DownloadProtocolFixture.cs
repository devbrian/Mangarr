using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.IndexerTests
{
    [TestFixture]
    public class DownloadProtocolFixture
    {
        [Test]
        public void http_is_value_3()
        {
            ((int)DownloadProtocol.Http).Should().Be(3);
        }

        [Test]
        public void existing_values_unchanged()
        {
            ((int)DownloadProtocol.Unknown).Should().Be(0);
            ((int)DownloadProtocol.Usenet).Should().Be(1);
            ((int)DownloadProtocol.Torrent).Should().Be(2);
        }

        [Test]
        public void enum_has_four_values()
        {
            Enum.GetValues<DownloadProtocol>().Length.Should().Be(4);
        }
    }
}
