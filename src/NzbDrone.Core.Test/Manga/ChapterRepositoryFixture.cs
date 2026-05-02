using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;
using Chapter = NzbDrone.Core.Manga.Chapter;
using ChapterRepository = NzbDrone.Core.Manga.ChapterRepository;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 1 live fixture for IChapterRepository — DECIMAL(10,3) round-trip + IsSynthetic
    // round-trip per D-12 + D-17. Plan 02-03 lands ChapterRepository.
    [TestFixture]
    public class ChapterRepositoryFixture : DbTest<ChapterRepository, Chapter>
    {
        private Chapter BuildChapter(int mangaId = 1, decimal chapterNumber = 1m, string lang = "en", bool synthetic = false)
        {
            return new Chapter
            {
                MangaId = mangaId,
                ChapterNumber = chapterNumber,
                ChapterType = ChapterType.Regular,
                Title = $"Chapter {chapterNumber}",
                TranslatedLanguage = lang,
                IsSynthetic = synthetic,
                Monitored = true,
            };
        }

        // D-12: ChapterNumber widens from DECIMAL(10,2) to DECIMAL(10,3) in Migration 002;
        // 1.123m must survive the round-trip after Plan 02-03 lands ChapterRepository.
        [Test]
        public void Insert_persists_decimal_chapter_number_with_3_decimals()
        {
            var chapter = BuildChapter(chapterNumber: 1.123m);
            Subject.Insert(chapter);

            var fetched = Subject.Get(chapter.Id);
            fetched.ChapterNumber.Should().Be(1.123m);
        }

        [Test]
        public void Find_by_manga_chapter_language_returns_match()
        {
            Subject.Insert(BuildChapter(mangaId: 1, chapterNumber: 5m, lang: "en"));
            Subject.Insert(BuildChapter(mangaId: 1, chapterNumber: 5m, lang: "es"));
            Subject.Insert(BuildChapter(mangaId: 2, chapterNumber: 5m, lang: "en"));

            var found = Subject.Find(1, 5m, "es");

            found.Should().NotBeNull();
            found.MangaId.Should().Be(1);
            found.ChapterNumber.Should().Be(5m);
            found.TranslatedLanguage.Should().Be("es");
        }

        [Test]
        public void GetByMangaId_returns_all_for_manga()
        {
            Subject.Insert(BuildChapter(mangaId: 1, chapterNumber: 1m));
            Subject.Insert(BuildChapter(mangaId: 1, chapterNumber: 2m));
            Subject.Insert(BuildChapter(mangaId: 2, chapterNumber: 1m));

            var found = Subject.GetByMangaId(1);

            found.Should().HaveCount(2);
            found.All(c => c.MangaId == 1).Should().BeTrue();
        }

        [Test]
        public void Update_persists_changes()
        {
            var chapter = BuildChapter();
            Subject.Insert(chapter);

            chapter.Title = "Updated title";
            chapter.Monitored = false;
            Subject.Update(chapter);

            var fetched = Subject.Get(chapter.Id);
            fetched.Title.Should().Be("Updated title");
            fetched.Monitored.Should().BeFalse();
        }

        [Test]
        public void Delete_removes_row()
        {
            var chapter = BuildChapter();
            Subject.Insert(chapter);

            Subject.Delete(chapter.Id);

            Subject.All().Should().BeEmpty();
        }

        [Test]
        public void IsSynthetic_round_trips()
        {
            Subject.Insert(BuildChapter(mangaId: 1, chapterNumber: 1m, synthetic: true));
            Subject.Insert(BuildChapter(mangaId: 1, chapterNumber: 2m, synthetic: false));

            var synthetic = Subject.GetSyntheticByMangaId(1);

            synthetic.Should().HaveCount(1);
            synthetic.Single().ChapterNumber.Should().Be(1m);
            synthetic.Single().IsSynthetic.Should().BeTrue();
        }
    }
}
