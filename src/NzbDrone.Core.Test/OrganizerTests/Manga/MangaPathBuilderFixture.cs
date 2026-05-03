using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.OrganizerTests.Manga
{
    // Wave 0 stub — replaced by Wave 3 plan 05-06.
    // Per D-15: flat folder layout `<root>/<Manga Title>/<Chapter NNN>.cbz` (two levels deep).
    [TestFixture]
    public class MangaPathBuilderFixture : CoreTest
    {
        [Test]
        public void Wave0_stub_replace_in_phase5_plan_05_06()
        {
            Assert.Ignore("Wave 0 stub — Phase 5 plan 05-06 replaces this with D-15 flat-folder layout assertions per 05-VALIDATION.md.");
        }
    }
}
