using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.MangaDex
{
    // Wave 0 scaffold for MangaDexMetadataSource (D-16 — IsPrimary=true by default).
    // RED until Plan 02-06.
    //
    // Production-shape this fixture will exercise once 02-06 lands:
    //   public class MangaDexMetadataSourceFixture : CoreTest<MangaDexMetadataSource>
    //
    // Pitfall 7 anchor (Plan 02-01 acceptance): the cross-source link extraction must
    // call int.TryParse on the JSON `links.al` and `links.mal` STRING values (the
    // MangaDex API ships these as strings, not ints).
    [TestFixture]
    public class MangaDexMetadataSourceFixture : CoreTest
    {
        // Verifies links.al → AniList id integer extraction via int.TryParse.
        [Test]
        [Ignore("RED — Plan 02-06 lands MangaDexMetadataSource.GetMangaInfo.")]
        public void GetMangaInfo_extracts_links_al_via_int_TryParse()
            => Assert.Inconclusive("Plan 02-06 — int.TryParse on links.al string");

        // Verifies links.mal → MAL id integer extraction via int.TryParse.
        [Test]
        [Ignore("RED — Plan 02-06 lands MangaDexMetadataSource.GetMangaInfo.")]
        public void GetMangaInfo_extracts_links_mal_via_int_TryParse()
            => Assert.Inconclusive("Plan 02-06 — int.TryParse on links.mal string");

        [Test]
        [Ignore("RED — Plan 02-06 lands MangaDexMetadataSource.Search.")]
        public void Search_returns_results_from_canned_response()
            => Assert.Inconclusive("Plan 02-06");

        [Test]
        [Ignore("RED — Plan 02-06 lands 404 → MangaNotFoundException mapping.")]
        public void GetMangaInfo_throws_MangaNotFoundException_on_404()
            => Assert.Inconclusive("Plan 02-06");
    }
}
