using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests.Manga
{
    // Wave 0 stub — replaced by Wave 4 plan 05-07.
    // F-01-class regression mitigation per 05-VALIDATION.md Wave 0 Requirements + 05-RESEARCH.md Pitfall 5.
    // The replacement test MUST round-trip:
    //   ReleaseInfo → MangaParsingService.Map → MangaDownloadDecisionMaker.GetSearchDecision
    //   → MangaDownloadDecisionComparer → top-decision
    // and assert the TPROFILE outer gate AND CF inner score AND comparer ranking ALL fire end-to-end.
    [TestFixture]
    public class MangaDownloadDecisionMakerEndToEndFixture : CoreTest
    {
        [Test]
        public void Wave0_stub_replace_in_phase5_plan_05_07()
        {
            Assert.Ignore("Wave 0 stub — Phase 5 plan 05-07 replaces this with the F-01-class regression mitigation per 05-VALIDATION.md.");
        }
    }
}
