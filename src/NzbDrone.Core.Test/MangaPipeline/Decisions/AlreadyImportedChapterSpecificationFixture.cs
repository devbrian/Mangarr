using NUnit.Framework;

namespace NzbDrone.Core.Test.MangaPipeline.Decisions
{
    // Phase 6 Wave 1 BLOCKING fixture — D-21 STUB-replacement target + BL-01 regression guard.
    // Wired by Plan 06-03 (ChapterHistoryService) + the Plan that flips the
    // AlreadyImportedChapterSpecification STUB body.
    //
    // BL-01 cross-domain ID-collision regression: the spec MUST query
    // ChapterHistory.ChapterId — NOT EpisodeHistory.EpisodeId. Test
    // Queries_ChapterHistory_only seeds an EpisodeHistory row with
    // EpisodeId == chapterId of an unrelated manga chapter and asserts the
    // spec accepts (Decision.Accept) the manga release. If the bug is present
    // the assertion fails.
    [TestFixture]
    public class AlreadyImportedChapterSpecificationFixture : MangaPipelineTestBase
    {
        [Test]
        [Ignore("Phase 6 Wave 1 — wired by Plan 06-03 (ChapterHistoryService) + D-21 STUB replacement; BL-01 fix")]
        public void Queries_ChapterHistory_only()
        {
            Assert.Fail("STUB — Wave 1 — BL-01 regression guard");
        }

        [Test]
        [Ignore("Phase 6 Wave 1 — wired by Plan 06-03 (ChapterHistoryService) + D-21 STUB replacement")]
        public void Rejects_when_imported()
        {
            Assert.Fail("STUB — Wave 1");
        }

        [Test]
        [Ignore("Phase 6 Wave 1 — wired by Plan 06-03 (ChapterHistoryService) + D-21 STUB replacement")]
        public void Accepts_when_no_import_history()
        {
            Assert.Fail("STUB — Wave 1");
        }
    }
}
