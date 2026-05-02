using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 0 scaffold for ChapterListService — D-17 three-strategy synthesis.
    // RED until Plan 02-09 lands ChapterListService.
    //
    // BCP-47 sentinel reminder: synthetic chapter rows MUST set TranslatedLanguage = "und"
    // per RESEARCH §Open Question 3 (NOT null — null breaks query planner index utilization
    // on SQLite and confuses the ChapterListUpdatedEvent consumer set).
    [TestFixture]
    public class ChapterListServiceFixture : CoreTest
    {
        // Strategy 1 (MangaDex linked): replace pre-existing IsSynthetic=true rows in place
        // by chapter number, flipping IsSynthetic=false; preserve row Id.
        [Test]
        [Ignore("RED — Plan 02-09 lands ChapterListService Strategy 1.")]
        public void Strategy1_MangaDex_linked_replaces_synthetic_rows_in_place_by_chapter_number()
            => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 lands ChapterListService Strategy 1.")]
        public void Strategy1_MangaDex_linked_inserts_new_chapters_not_previously_present()
            => Assert.Inconclusive("Plan 02-09");

        // Strategy 2 (no MangaDex link, primary returned total chapter count): synthesize
        // N rows with TranslatedLanguage = "und" sentinel.
        [Test]
        [Ignore("RED — Plan 02-09 lands Strategy 2 synthesis with \"und\" BCP-47 sentinel.")]
        public void Strategy2_no_MangaDex_link_synthesizes_N_rows_with_und_sentinel()
            => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 Strategy 2 idempotency.")]
        public void Strategy2_already_synthesized_does_not_resynthesize()
            => Assert.Inconclusive("Plan 02-09");

        // Strategy 3: primary returns null total → log warning + return empty list.
        [Test]
        [Ignore("RED — Plan 02-09 lands Strategy 3 empty-list fallback.")]
        public void Strategy3_null_chapter_count_logs_warning_and_returns_empty()
            => Assert.Inconclusive("Plan 02-09");
    }
}
