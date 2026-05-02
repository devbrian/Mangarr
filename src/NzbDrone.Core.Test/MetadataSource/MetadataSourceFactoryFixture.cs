using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Wave 0 scaffold for MetadataSourceFactory — META-05 + threat T-CONFIG-DRIFT-01.
    // RED until Plan 02-05 lands the IMetadataSource scaffold + factory.
    //
    // Production-shape this fixture will exercise once 02-05 lands:
    //   public class MetadataSourceFactoryFixture : CoreTest<MetadataSourceFactory>
    //
    // Acceptance literal anchors required by Plan 02-01 Task 2 acceptance:
    //   * All_three_providers_resolved (test method below)
    //   * SetPrimary, GetPrimary (test methods below)
    [TestFixture]
    public class MetadataSourceFactoryFixture : CoreTest
    {
        // Per Pitfall 4 in 02-RESEARCH: assert injected IEnumerable<IMetadataSource> count == 3.
        [Test]
        [Ignore("RED — Plan 02-05 lands IMetadataSource ThingiProvider auto-discovery.")]
        public void All_three_providers_resolved() => Assert.Inconclusive("Plan 02-05");

        // SetPrimary(2) flips id=1 IsPrimary=true → false and id=2 IsPrimary=false → true
        // (atomic). Covers META-05 + threat T-CONFIG-DRIFT-01.
        [Test]
        [Ignore("RED — Plan 02-05 lands MetadataSourceFactory.SetPrimary atomic demote.")]
        public void SetPrimary_demotes_prior_primary_when_promoting_another()
            => Assert.Inconclusive("Plan 02-05");

        [Test]
        [Ignore("RED — Plan 02-05 lands MetadataSourceFactory.GetPrimary.")]
        public void GetPrimary_returns_the_one_with_IsPrimary_true()
            => Assert.Inconclusive("Plan 02-05");

        [Test]
        [Ignore("RED — Plan 02-05 lands MetadataSourceFactory.SetPrimary id-validation.")]
        public void SetPrimary_throws_when_id_not_found()
            => Assert.Inconclusive("Plan 02-05");
    }
}
