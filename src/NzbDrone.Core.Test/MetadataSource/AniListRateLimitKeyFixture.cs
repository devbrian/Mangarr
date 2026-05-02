using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Wave 0 scaffold — verifies that every outbound HttpRequest from the AniList
    // provider carries RateLimitKey == "anilist". Covers META-05 + threat T-DOS-01.
    // RED until Plan 02-07.
    [TestFixture]
    public class AniListRateLimitKeyFixture : CoreTest
    {
        // request.HttpRequest.RateLimitKey must equal "anilist".
        [Test]
        [Ignore("RED — Plan 02-07 lands the RateLimitKey = \"anilist\" tag.")]
        public void Outbound_request_has_RateLimitKey_equal_anilist()
            => Assert.Inconclusive("Plan 02-07 — RateLimitKey literal: \"anilist\"");
    }
}
