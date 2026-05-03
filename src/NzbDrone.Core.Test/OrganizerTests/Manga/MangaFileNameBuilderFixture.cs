using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.OrganizerTests.Manga
{
    // Wave 0 stub — replaced by Wave 3 plan 05-06.
    // Per READER-02 + 05-RESEARCH.md Pitfall 7 (decimal padding edge cases).
    // The replacement test MUST cover D-14 token set including {Chapter.Number:000} / :0000 / :000.0
    // padding edge cases AND a [SetCulture("de-DE")] regression cell asserting "042.5"
    // (NOT "0042,5" — German decimal separator) per Phase 4 LEARNINGS InvariantCulture discipline.
    [TestFixture]
    public class MangaFileNameBuilderFixture : CoreTest
    {
        [Test]
        public void Wave0_stub_replace_in_phase5_plan_05_06()
        {
            Assert.Ignore("Wave 0 stub — Phase 5 plan 05-06 replaces this with full D-14 token coverage + S10 culture regression cells per 05-VALIDATION.md.");
        }
    }
}
