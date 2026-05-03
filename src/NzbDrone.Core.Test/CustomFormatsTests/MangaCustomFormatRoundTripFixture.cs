using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormatsTests
{
    // Wave 0 stub — replaced by Wave 4 plan 05-07.
    // Per CF-04: Custom Formats can be exported and imported as JSON.
    // The replacement test MUST export a CF containing TranslatedLanguageSpec +
    // ScanlationGroupSpec + SourceKeySpec + ChapterTypeSpec, then re-import lossless.
    [TestFixture]
    public class MangaCustomFormatRoundTripFixture : CoreTest
    {
        [Test]
        public void Wave0_stub_replace_in_phase5_plan_05_07()
        {
            Assert.Ignore("Wave 0 stub — Phase 5 plan 05-07 replaces this with CF-04 round-trip across the 4 NEW manga spec types per 05-VALIDATION.md.");
        }
    }
}
