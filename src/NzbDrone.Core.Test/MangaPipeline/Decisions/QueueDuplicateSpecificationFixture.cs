using NUnit.Framework;

namespace NzbDrone.Core.Test.MangaPipeline.Decisions
{
    // Phase 6 Wave 1 BLOCKING fixture — D-20 STUB-replacement target.
    // Wired by Plan 06-05 (MangaQueueService) + the Plan that flips the
    // QueueDuplicateSpecification STUB body to query the in-flight projection.
    [TestFixture]
    public class QueueDuplicateSpecificationFixture : MangaPipelineTestBase
    {
        [Test]
        [Ignore("Phase 6 Wave 1 — wired by Plan 06-05 (MangaQueueService) + D-20 STUB replacement")]
        public void Rejects_when_in_queue()
        {
            Assert.Fail("STUB — Wave 1");
        }

        [Test]
        [Ignore("Phase 6 Wave 1 — wired by Plan 06-05 (MangaQueueService) + D-20 STUB replacement")]
        public void Accepts_when_queue_empty()
        {
            Assert.Fail("STUB — Wave 1");
        }
    }
}
