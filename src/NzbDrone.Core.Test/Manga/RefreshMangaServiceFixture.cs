using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 0 scaffold for RefreshMangaService — META-04. RED until Plan 02-09.
    //
    // Production-shape this fixture will exercise once 02-09 lands:
    //   public class RefreshMangaServiceFixture : CoreTest<RefreshMangaService>
    [TestFixture]
    public class RefreshMangaServiceFixture : CoreTest
    {
        [Test]
        [Ignore("RED — Plan 02-09 lands RefreshMangaService.")]
        public void Execute_calls_primary_GetMangaInfo_for_each_id()
            => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 routes by IsPrimary.")]
        public void Execute_uses_MangaDexId_when_primary_is_MangaDex()
            => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 routes by IsPrimary.")]
        public void Execute_uses_AniListId_when_primary_is_AniList()
            => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 routes by IsPrimary.")]
        public void Execute_uses_MalId_when_primary_is_MAL()
            => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 skips when primary's source ID is null.")]
        public void Execute_skips_manga_with_no_source_id_for_active_primary()
            => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 publishes MangaUpdatedEvent + ChapterListUpdatedEvent.")]
        public void Execute_publishes_MangaUpdatedEvent_and_ChapterListUpdatedEvent()
            => Assert.Inconclusive("Plan 02-09");
    }
}
