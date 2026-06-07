using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MangaStats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;
using Chapter = NzbDrone.Core.Manga.Chapter;

namespace NzbDrone.Core.Test.MangaStatsTests
{
    // Issue #335 — real-SQL coverage for the previously-dead MangaStatisticsRepository.
    // This is where the aggregation semantics now live (the controller's former inline
    // ComputeStatistics is gone), so the "224/225" denominator regression and the
    // FirstReleaseDate column-name fix are pinned here against an actual database.
    //
    // DbTest<MangaStatisticsRepository, Chapter> resolves the repo via Mocker (it takes
    // IMainDatabase, which DbTest supplies); the Chapter type only seeds the Storage helper.
    [TestFixture]
    public class MangaStatisticsRepositoryFixture : DbTest<MangaStatisticsRepository, Chapter>
    {
        private void GivenChapter(int mangaId, decimal chapterNumber, bool monitored, int? chapterFileId, DateTime? firstReleaseDate = null)
        {
            Db.Insert(new Chapter
            {
                MangaId = mangaId,
                ChapterNumber = chapterNumber,
                ChapterType = ChapterType.Regular,
                Title = $"Chapter {chapterNumber}",
                FirstReleaseDate = firstReleaseDate,
                Monitored = monitored,
                ChapterFileId = chapterFileId,
            });
        }

        private void GivenChapterFile(int mangaId, long size, string scanlationGroup, string translatedLanguage)
        {
            Db.Insert(new ChapterFile
            {
                MangaId = mangaId,
                ChapterId = 0,
                RelativePath = $"{scanlationGroup}.cbz",
                Path = $"/manga/{scanlationGroup}.cbz",
                Size = size,
                DateAdded = DateTime.UtcNow,
                ScanlationGroup = scanlationGroup,
                TranslatedLanguage = translatedLanguage,
            });
        }

        [Test]
        public void should_exclude_unmonitored_fileless_chapter_from_ChapterCount_denominator()
        {
            // The "224/225" regression, now enforced at the SQL layer.
            GivenChapter(mangaId: 1, chapterNumber: 1m, monitored: true, chapterFileId: null);   // monitored, missing
            GivenChapter(mangaId: 1, chapterNumber: 2m, monitored: true, chapterFileId: 10);      // monitored, downloaded
            GivenChapter(mangaId: 1, chapterNumber: 0m, monitored: false, chapterFileId: null);   // unmonitored + fileless -> EXCLUDED
            GivenChapter(mangaId: 1, chapterNumber: 3m, monitored: false, chapterFileId: 20);     // unmonitored but downloaded -> kept

            var stats = Subject.MangaStatistics(1).Single();

            stats.TotalChapterCount.Should().Be(4);
            stats.ChapterCount.Should().Be(3);            // denominator: monitored OR has-file (chapter 0 drops out)
            stats.ChapterFileCount.Should().Be(2);
            stats.MonitoredChapterCount.Should().Be(2);
            stats.ChapterFileCount.Should().BeLessOrEqualTo(stats.ChapterCount);
        }

        [Test]
        public void should_query_chapters_with_populated_FirstReleaseDate_without_throwing()
        {
            // Direct regression for the latent SQL defects this repo carried while dead:
            //   1. it selected a non-existent "ReleaseDate" column (the real column is
            //      "FirstReleaseDate") — any call threw at runtime; and
            //   2. the MIN/MAX(CASE ... FirstReleaseDate ...) date aggregates lost SQLite type
            //      affinity and threw an InvalidCastException in DapperUtcConverter.
            // Exercising the query against chapters with populated (and NULL) release dates proves
            // both are resolved and the counts are unaffected by the presence of release dates.
            var early = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var late = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

            GivenChapter(mangaId: 1, chapterNumber: 1m, monitored: true, chapterFileId: null, firstReleaseDate: early);
            GivenChapter(mangaId: 1, chapterNumber: 2m, monitored: true, chapterFileId: null, firstReleaseDate: late);
            GivenChapter(mangaId: 1, chapterNumber: 3m, monitored: true, chapterFileId: null, firstReleaseDate: null); // NULL is fine

            var stats = Subject.MangaStatistics(1).Single();

            stats.TotalChapterCount.Should().Be(3);
            stats.ChapterCount.Should().Be(3);
            stats.MonitoredChapterCount.Should().Be(3);
        }

        [Test]
        public void should_roll_up_size_and_scanlation_and_language_sets_from_chapter_files()
        {
            GivenChapter(mangaId: 1, chapterNumber: 1m, monitored: true, chapterFileId: 1);
            GivenChapterFile(mangaId: 1, size: 1000, scanlationGroup: "Group A", translatedLanguage: "en");
            GivenChapterFile(mangaId: 1, size: 500, scanlationGroup: "Group B", translatedLanguage: "ja");

            var stats = Subject.MangaStatistics(1).Single();

            stats.SizeOnDisk.Should().Be(1500);
            stats.ScanlationGroups.Should().BeEquivalentTo("Group A", "Group B");
            stats.TranslatedLanguages.Should().Contain(Language.English);
            stats.TranslatedLanguages.Should().Contain(Language.Japanese);
        }

        [Test]
        public void should_isolate_statistics_per_manga()
        {
            GivenChapter(mangaId: 1, chapterNumber: 1m, monitored: true, chapterFileId: null);
            GivenChapter(mangaId: 2, chapterNumber: 1m, monitored: true, chapterFileId: null);
            GivenChapter(mangaId: 2, chapterNumber: 2m, monitored: true, chapterFileId: null);

            Subject.MangaStatistics(1).Single().TotalChapterCount.Should().Be(1);
            Subject.MangaStatistics(2).Single().TotalChapterCount.Should().Be(2);

            // The all-manga overload returns one row per manga.
            Subject.MangaStatistics().Should().HaveCount(2);
        }
    }
}
