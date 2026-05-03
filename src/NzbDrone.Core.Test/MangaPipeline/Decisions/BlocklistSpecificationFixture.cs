using NUnit.Framework;

namespace NzbDrone.Core.Test.MangaPipeline.Decisions
{
    // Phase 6 Wave 1 BLOCKING fixture — D-19 STUB-replacement target.
    // Wired by Plan 06-04 (MangaBlocklistService) + the Plan that flips the
    // BlocklistSpecification STUB body to call _blocklistService.Blocklisted.
    [TestFixture]
    public class BlocklistSpecificationFixture : MangaPipelineTestBase
    {
        [Test]
        [Ignore("Phase 6 Wave 1 — wired by Plan 06-04 (MangaBlocklistService) + D-19 STUB replacement")]
        public void Rejects_when_blocklisted()
        {
            Assert.Fail("STUB — Wave 1");
        }

        [Test]
        [Ignore("Phase 6 Wave 1 — wired by Plan 06-04 (MangaBlocklistService) + D-19 STUB replacement")]
        public void Accepts_when_not_blocklisted()
        {
            Assert.Fail("STUB — Wave 1");
        }
    }
}
