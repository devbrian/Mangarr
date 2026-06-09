using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // #357 D-3 coverage for the two MangaMonitor values the enum-unification added:
    //   * Existing -> monitor chapters ALREADY released (FirstReleaseDate.HasValue && <= UtcNow)
    //   * First    -> monitor ONLY the lowest ChapterNumber
    // Plus a sanity pin for the pre-existing Latest (highest ChapterNumber) so the First/Latest
    // Min/Max pair is locked together.
    //
    // Construction mirrors the sibling ChapterServiceFixture (CoreTest<TSubject> + AutoMoq).
    // The chapter set is seeded with mixed FirstReleaseDate (past / future / null) and distinct
    // ChapterNumbers; the service mutates the list in place and calls UpdateMany, so asserting on
    // the seeded list after Execute reflects the final Monitored flags.
    [TestFixture]
    public class ChapterMonitoredServiceFixture : CoreTest<ChapterMonitoredService>
    {
        private Manga.Manga _manga;
        private List<Chapter> _chapters;

        private Chapter _pastLow;     // ch 1, released yesterday
        private Chapter _pastMid;     // ch 2, released yesterday
        private Chapter _futureHigh;  // ch 3, releases tomorrow
        private Chapter _nullDate;    // ch 4, no release date

        [SetUp]
        public void Setup()
        {
            _manga = new Manga.Manga { Id = 7, Title = "Test Manga" };

            _pastLow = new Chapter { Id = 1, MangaId = 7, ChapterNumber = 1m, FirstReleaseDate = DateTime.UtcNow.AddDays(-1) };
            _pastMid = new Chapter { Id = 2, MangaId = 7, ChapterNumber = 2m, FirstReleaseDate = DateTime.UtcNow.AddDays(-1) };
            _futureHigh = new Chapter { Id = 3, MangaId = 7, ChapterNumber = 3m, FirstReleaseDate = DateTime.UtcNow.AddDays(1) };
            _nullDate = new Chapter { Id = 4, MangaId = 7, ChapterNumber = 4m, FirstReleaseDate = null };

            _chapters = new List<Chapter> { _pastLow, _pastMid, _futureHigh, _nullDate };

            Mocker.GetMock<IChapterService>()
                  .Setup(s => s.GetChaptersByManga(7))
                  .Returns(_chapters);
        }

        private void Run(MangaMonitor monitor)
        {
            Subject.SetChapterMonitoredStatus(_manga, new AddMangaOptions { Monitor = monitor });
        }

        [Test]
        public void Existing_monitors_only_already_released_chapters()
        {
            Run(MangaMonitor.Existing);

            _pastLow.Monitored.Should().BeTrue("ch1 was released in the past");
            _pastMid.Monitored.Should().BeTrue("ch2 was released in the past");
            _futureHigh.Monitored.Should().BeFalse("ch3 releases in the future");
            _nullDate.Monitored.Should().BeFalse("ch4 has no release date (treated as unreleased)");

            Mocker.GetMock<IChapterService>().Verify(s => s.UpdateMany(_chapters), Times.Once);
        }

        [Test]
        public void First_monitors_only_the_lowest_numbered_chapter()
        {
            Run(MangaMonitor.First);

            _pastLow.Monitored.Should().BeTrue("ch1 is the lowest ChapterNumber");
            _chapters.Where(c => c.ChapterNumber != 1m)
                     .Should().OnlyContain(c => !c.Monitored, "only the first chapter is monitored");

            Mocker.GetMock<IChapterService>().Verify(s => s.UpdateMany(_chapters), Times.Once);
        }

        [Test]
        public void Latest_monitors_only_the_highest_numbered_chapter()
        {
            // Sanity pin for the pre-existing Latest arm — locks the First/Latest Min/Max pair.
            Run(MangaMonitor.Latest);

            _nullDate.Monitored.Should().BeTrue("ch4 is the highest ChapterNumber (Latest = Max)");
            _chapters.Where(c => c.ChapterNumber != 4m)
                     .Should().OnlyContain(c => !c.Monitored, "only the latest chapter is monitored");
        }
    }
}
