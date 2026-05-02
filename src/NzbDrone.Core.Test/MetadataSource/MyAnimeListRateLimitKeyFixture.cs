using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Wave 0 scaffold — verifies that every outbound HttpRequest from the MyAnimeList
    // provider carries RateLimitKey == "myanimelist". Covers META-05 + threat T-DOS-01.
    // RED until Plan 02-08.
    [TestFixture]
    public class MyAnimeListRateLimitKeyFixture : CoreTest
    {
        // request.HttpRequest.RateLimitKey must equal "myanimelist".
        [Test]
        [Ignore("RED — Plan 02-08 lands the RateLimitKey = \"myanimelist\" tag.")]
        public void Outbound_request_has_RateLimitKey_equal_myanimelist()
            => Assert.Inconclusive("Plan 02-08 — RateLimitKey literal: \"myanimelist\"");
    }
}
