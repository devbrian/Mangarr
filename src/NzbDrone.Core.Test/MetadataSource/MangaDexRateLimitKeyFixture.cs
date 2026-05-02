using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Wave 0 scaffold — verifies that every outbound HttpRequest from the MangaDex
    // provider carries RateLimitKey == "mangadex". Covers META-05 + threat T-DOS-01
    // (per-SourceKey rate-limit budget enforcement). RED until Plan 02-06.
    [TestFixture]
    public class MangaDexRateLimitKeyFixture : CoreTest
    {
        // request.HttpRequest.RateLimitKey must equal "mangadex".
        [Test]
        [Ignore("RED — Plan 02-06 lands the RateLimitKey = \"mangadex\" tag.")]
        public void Outbound_request_has_RateLimitKey_equal_mangadex()
            => Assert.Inconclusive("Plan 02-06 — RateLimitKey literal: \"mangadex\"");
    }
}
