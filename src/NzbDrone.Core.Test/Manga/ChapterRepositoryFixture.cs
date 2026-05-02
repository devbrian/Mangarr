using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 0 scaffold for IChapterRepository — DECIMAL(10,3) round-trip + IsSynthetic
    // round-trip per D-12 + D-17. RED until Plan 02-03.
    [TestFixture]
    public class ChapterRepositoryFixture : CoreTest
    {
        // D-12: ChapterNumber widens from DECIMAL(10,2) to DECIMAL(10,3) in Migration 002;
        // 1.123m must survive the round-trip after Plan 02-03 lands ChapterRepository.
        [Test]
        [Ignore("RED — Plan 02-03 lands ChapterRepository.")]
        public void Insert_persists_decimal_chapter_number_with_3_decimals()
            => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands ChapterRepository.")]
        public void Find_by_manga_chapter_language_returns_match()
            => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands ChapterRepository.")]
        public void GetByMangaId_returns_all_for_manga()
            => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands ChapterRepository.")]
        public void Update_persists_changes()
            => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands ChapterRepository.")]
        public void Delete_removes_row()
            => Assert.Inconclusive("Plan 02-03");

        [Test]
        [Ignore("RED — Plan 02-03 lands ChapterRepository.")]
        public void IsSynthetic_round_trips()
            => Assert.Inconclusive("Plan 02-03");
    }
}
