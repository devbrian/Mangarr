// Sonarr divergence: REWRITTEN per Phase 16 STRUCT-01 + STRUCT-04 — see DIVERGENCE.md.
// Drops 3-arg Find(int,decimal,string) overload + IsSynthetic round-trip per Pitfalls 3 + STRUCT-03.
// Adds UNIQUE-violation expected-throw (STRUCT-01) + FirstReleaseDate round-trip (STRUCT-04 / D-02).
// Plan 16-02 (Wave 1) landed the schema + property changes that turn this fixture GREEN;
// the Wave-0 `#if PHASE_16_WAVE_1` guard was removed inline (Plan 16-01 hand-off
// option (b)).
using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;
using Chapter = NzbDrone.Core.Manga.Chapter;
using ChapterRepository = NzbDrone.Core.Manga.ChapterRepository;

namespace NzbDrone.Core.Test.MangaTests
{
    // Wave 1 live fixture for IChapterRepository — Phase 16 schema cutover (STRUCT-01 +
    // STRUCT-04). Plan 16-02 landed Chapter.FirstReleaseDate + the composite UNIQUE
    // constraint on Chapters(MangaId, ChapterNumber).
    [TestFixture]
    public class ChapterRepositoryFixture
        : DbTest<ChapterRepository, Chapter>
    {
        private Chapter BuildChapter(int mangaId = 1, decimal chapterNumber = 1m, DateTime? firstReleaseDate = null)
        {
            return new Chapter
            {
                MangaId = mangaId,
                ChapterNumber = chapterNumber,
                ChapterType = ChapterType.Regular,
                Title = $"Chapter {chapterNumber}",
                FirstReleaseDate = firstReleaseDate,
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

        // STRUCT-01 acceptance: composite UNIQUE on (MangaId, ChapterNumber) enforces
        // one canonical row per (manga, chapter#). Dialect-agnostic FluentAssertions
        // wildcard per Pitfall 7 — works on both SQLite and Postgres.
        [Test]
        public void Insert_duplicate_MangaId_ChapterNumber_throws_unique_violation()
        {
            Subject.Insert(BuildChapter(mangaId: 1, chapterNumber: 5m));
            Action act = () => Subject.Insert(BuildChapter(mangaId: 1, chapterNumber: 5m));
            act.Should().Throw<Exception>().Where(e =>
                e.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase));
        }

        // STRUCT-04 acceptance: Chapter.FirstReleaseDate (D-02) round-trips through the
        // repository. Modeled on Insert_persists_decimal_chapter_number_with_3_decimals.
        [Test]
        public void Insert_persists_FirstReleaseDate_round_trip()
        {
            var when = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var c = BuildChapter(firstReleaseDate: when);
            Subject.Insert(c);
            Subject.Get(c.Id).FirstReleaseDate.Should().Be(when);
        }

        // STRUCT-04 release-grain-agnostic guard: Plan 16-03 Task 4 reflection check.
        // ChaptersWhereCutoffUnmet at ChapterRepository.cs:88-139 already filters by
        // Manga.TranslationProfileId / CustomFormatProfileId — never reads
        // Chapter.TranslatedLanguage. Living-documentation guard: confirm the
        // post-Phase-16 Chapter has NO TranslatedLanguage property at all (any future
        // accidental reintroduction would silently re-couple the cutoff query to the
        // pre-Phase-16 grain).
        [Test]
        public void Chapter_does_not_carry_TranslatedLanguage_after_Phase_16()
        {
            // STRUCT-04 removes Chapter.TranslatedLanguage. Any reflection access
            // returns null. Compile-time guard wouldn't surface a regression silently
            // (a re-added property would compile clean) — runtime guard via reflection
            // catches the regression even if no code reads the field directly.
            typeof(Chapter)
                .GetProperty("TranslatedLanguage")
                .Should().BeNull(
                    "Phase 16 STRUCT-04 lifted language to ChapterRelease; "
                    + "Chapter must remain language-free at the (MangaId, ChapterNumber) grain");
        }
    }
}
