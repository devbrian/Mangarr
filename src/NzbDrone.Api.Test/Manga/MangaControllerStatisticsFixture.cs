using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Mangarr.Api.V5.Manga;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Test.Common;
using ChapterModel = NzbDrone.Core.Manga.Chapter;

namespace NzbDrone.Api.Test.Manga
{
    // Regression fixture for the "224 / 225" chapter-monitoring count bug
    // (.planning/debug/chapter-unmonitor-count.md): MangaController.ComputeStatistics
    // set the progress denominator ChapterCount = chapters.Count (every chapter), so an
    // unmonitored chapter with no file kept inflating the total even though it should be
    // excluded. The fix makes ChapterCount the Sonarr SeriesStatistics.EpisodeCount peer
    // — Count(c => c.Monitored || c.ChapterFileId.HasValue) — while TotalChapterCount keeps
    // the true total.
    //
    // Tested through the public GetAll() endpoint because ComputeStatistics + MapResource
    // are private and GetResourceById is protected (cross-assembly). GetAll() calls
    // MapResource -> ComputeStatistics for each manga, attaching Statistics.
    //
    // Fixture lives under NzbDrone.Api.Test (not NzbDrone.Core.Test) for the same reason as
    // MangaControllerSignalRFixture: Mangarr.Core.Test does not project-reference
    // Mangarr.Api.V5; Mangarr.Api.Test does.
    [TestFixture]
    public class MangaControllerStatisticsFixture : TestBase<MangaController>
    {
        private NzbDrone.Core.Manga.Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 4)
                .With(m => m.Title = "The Greatest Estate Developer")
                .With(m => m.Images = new List<MediaCover>())
                .With(m => m.Genres = new List<string>())
                .With(m => m.Tags = new HashSet<int>())
                .Build();

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(new List<NzbDrone.Core.Manga.Manga> { _manga });
        }

        private static ChapterModel BuildChapter(int id, decimal number, bool monitored, int? chapterFileId)
        {
            return Builder<ChapterModel>.CreateNew()
                .With(c => c.Id = id)
                .With(c => c.MangaId = 4)
                .With(c => c.ChapterNumber = number)
                .With(c => c.Monitored = monitored)
                .With(c => c.ChapterFileId = chapterFileId)
                .Build();
        }

        private void GivenChapters(params ChapterModel[] chapters)
        {
            Mocker.GetMock<IChapterService>()
                  .Setup(s => s.GetChaptersByManga(4))
                  .Returns(chapters.ToList());

            // ComputeStatistics sums file sizes for chapters with a file; return empty so
            // the size lookup is a clean no-op (count assertions don't depend on size).
            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.Get(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<ChapterFile>());
        }

        private MangaStatisticsResource StatisticsFromGetAll()
        {
            var result = (Ok<List<MangaResource>>)Subject.GetAll();
            return result.Value.Single().Statistics;
        }

        [Test]
        public void should_exclude_unmonitored_chapter_with_no_file_from_ChapterCount_denominator()
        {
            // The bug repro: Chapter 0 is unmonitored AND has no file. Pre-fix the
            // denominator counted it (225); post-fix it must be excluded (224).
            GivenChapters(
                BuildChapter(id: 1, number: 0m, monitored: false, chapterFileId: null), // Chapter 0 — the unmonitored, fileless one
                BuildChapter(id: 2, number: 1m, monitored: true, chapterFileId: null),  // monitored, missing
                BuildChapter(id: 3, number: 2m, monitored: true, chapterFileId: 10));   // monitored, downloaded

            var stats = StatisticsFromGetAll();

            // 3 total, but Chapter 0 (unmonitored + no file) drops out of the denominator.
            stats.TotalChapterCount.Should().Be(3);
            stats.ChapterCount.Should().Be(2);
            stats.MonitoredChapterCount.Should().Be(2);
            stats.ChapterFileCount.Should().Be(1);

            // TV-shape alias fields mirror the chapter-shape canonical fields.
            stats.EpisodeCount.Should().Be(2);
            stats.TotalEpisodeCount.Should().Be(3);
        }

        [Test]
        public void should_still_count_unmonitored_chapter_that_has_a_file_in_ChapterCount()
        {
            // Invariant guard: an unmonitored-but-downloaded chapter must stay in the
            // denominator (the HasFile term), so chapterFileCount <= chapterCount always holds.
            GivenChapters(
                BuildChapter(id: 1, number: 0m, monitored: false, chapterFileId: 10), // unmonitored but on disk
                BuildChapter(id: 2, number: 1m, monitored: true, chapterFileId: 20));  // monitored, downloaded

            var stats = StatisticsFromGetAll();

            stats.TotalChapterCount.Should().Be(2);
            stats.ChapterCount.Should().Be(2);            // both count: one monitored, one has-file
            stats.MonitoredChapterCount.Should().Be(1);
            stats.ChapterFileCount.Should().Be(2);
            stats.ChapterFileCount.Should().BeLessOrEqualTo(stats.ChapterCount);
        }
    }
}
