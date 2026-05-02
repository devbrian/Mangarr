using NUnit.Framework;
using NzbDrone.Test.Common.Categories;

namespace NzbDrone.Integration.Test
{
    // Wave 0 integration scaffold for the developer manga-lookup endpoint
    // (META-01 + threat T-AUTHN-01). RED until Plan 02-10 lands MangaLookupController.
    //
    // Acceptance literal anchors required by Plan 02-01 Task 3:
    //   * /api/v5/manga/lookup?term=
    //   * X-Api-Key
    //
    // Production-shape this fixture will exercise once 02-10 lands:
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
        [Test]
        [Category("Integration")]
        [Ignore("RED — Plan 02-10 lands MangaLookupController + MangaResource at /api/v5/manga/lookup?term= (X-Api-Key required).")]
        public void Search_returns_results()
            => Assert.Inconclusive("Plan 02-10 — endpoint /api/v5/manga/lookup?term=, header X-Api-Key.");
    }
}
