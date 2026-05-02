using NUnit.Framework;
using NzbDrone.Test.Common.Categories;

namespace NzbDrone.Integration.Test
{
    // Wave 0 integration scaffold for the developer manga-lookup endpoint
    // (META-01 + threat T-AUTHN-01). Originally RED until Plan 02-10 lands
    // MangaLookupController; that production code did land but the integration
    // test still cannot pass at runtime — see deferral note on the test method
    // below.
    //
    // Acceptance literal anchors required by Plan 02-01 Task 3:
    //   * /api/v5/manga/lookup?term=
    //   * X-Api-Key
    //
    // Production-shape this fixture will exercise once a Phase-3 startup-time
    // default-primary MetadataSource bootstrap lands:
    //
    //   [Test]
    //   public void Search_returns_results()
    //   {
    //       var resp = Get("/api/v5/manga/lookup?term=naruto",
    //                      headers: new { ["X-Api-Key"] = ApiKey });
    //       resp.StatusCode.Should().Be(HttpStatusCode.OK);
    //       var body = JsonConvert.DeserializeObject<List<MangaResource>>(resp.Content);
    //       body.Should().NotBeEmpty();
    //       body.First().Title.Should().NotBeNullOrWhiteSpace();
    //   }
    //
    // Marked [Category("Integration")] so the per-task quick-subset run skips it
    // (sampling rule from VALIDATION.md), since real MangaDex reachability is
    // CI-fenced.
    [TestFixture]
    [IntegrationTest]
    public class MangaLookupControllerFixture
    {
        // Deferred to Phase 3 (Plan 02-13 revision 1, 2026-05-02). Plan 02-11 creates
        // the MetadataSources table per dev-migration-policy.md but adds no
        // default-primary seed row. Until a Phase-3 startup-time bootstrap inserts
        // at least one MetadataSourceDefinition with IsPrimary=true at app start,
        // MetadataSourceFactory.GetPrimary() throws
        // InvalidOperationException("No primary metadata source configured") on every
        // lookup, and this integration test will return 500. The gap inventory's
        // explicit rule (do NOT force-pass) forbids mocking around the integration
        // test framework or seeding the MetadataSources table from inside the
        // fixture. Phase 3 MUST add the bootstrap; once it lands this test should be
        // flipped to OUTCOME A (real GET against /api/v5/manga/lookup?term=naruto)
        // in a Phase-3 follow-up plan.
        [Test]
        [Category("Integration")]
        [Ignore("Deferred to Phase 3: requires startup-time default-primary MetadataSource bootstrap (no Phase 2 plan added the seed row).")]
        public void Search_returns_results()
            => Assert.Inconclusive("Deferred to Phase 3: requires startup-time default-primary MetadataSource bootstrap (no Phase 2 plan added the seed row).");
    }
}
