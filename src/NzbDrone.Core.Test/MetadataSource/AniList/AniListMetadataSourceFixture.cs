using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    // Wave 0 scaffold for AniListMetadataSource (D-25 — NEW file; the anime-side
    // ImportLists/AniList/AniListAPI.cs stays UNTOUCHED). RED until Plan 02-07.
    //
    // Production-shape this fixture will exercise once 02-07 lands:
    //   public class AniListMetadataSourceFixture : CoreTest<AniListMetadataSource>
    [TestFixture]
    public class AniListMetadataSourceFixture : CoreTest
    {
        [Test]
        [Ignore("RED — Plan 02-07 lands AniListMetadataSource.GetMangaInfo.")]
        public void GetMangaInfo_extracts_idMal_for_cross_source()
            => Assert.Inconclusive("Plan 02-07");

        // D-21 multi-axis confirm input — primary author from staff entries with role="Story".
        [Test]
        [Ignore("RED — Plan 02-07 lands the staff[] role=Story extraction.")]
        public void GetMangaInfo_extracts_primary_author_from_staff_role_Story()
            => Assert.Inconclusive("Plan 02-07");

        [Test]
        [Ignore("RED — Plan 02-07 lands chapters-field extraction.")]
        public void GetMangaInfo_extracts_total_chapter_count_from_chapters_field()
            => Assert.Inconclusive("Plan 02-07");

        [Test]
        [Ignore("RED — Plan 02-07 lands AniListMetadataSource.Search.")]
        public void Search_returns_results_from_Page_media()
            => Assert.Inconclusive("Plan 02-07");
    }
}
