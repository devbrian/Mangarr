using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Mangarr.Api.V5.Manga;
using Microsoft.AspNetCore.Http.HttpResults;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MangaStats;
using NzbDrone.Core.MediaCover;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Manga
{
    // Issue #335 — MangaController now links statistics from the SQL-aggregated
    // IMangaStatisticsService (Sonarr SeriesController pattern) instead of the inline
    // ComputeStatistics it carried as the F-05 stopgap. The denominator semantics
    // ("224/225" unmonitored-fileless exclusion) moved INTO the SQL repository and are
    // pinned by NzbDrone.Core.Test/MangaStatsTests/MangaStatisticsRepositoryFixture.
    //
    // This controller-level fixture verifies the WIRING: the model → resource mapping
    // (counts + sizeOnDisk + TV-shape aliases) and the F-05 always-present contract — a
    // manga absent from the aggregate query still gets a zeroed Statistics object, never null.
    //
    // Fixture lives under NzbDrone.Api.Test (not NzbDrone.Core.Test) because Mangarr.Core.Test
    // does not project-reference Mangarr.Api.V5; Mangarr.Api.Test does.
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

        private void GivenStatistics(params MangaStatistics[] statistics)
        {
            Mocker.GetMock<IMangaStatisticsService>()
                  .Setup(s => s.MangaStatistics())
                  .Returns(statistics.ToList());
        }

        private MangaStatisticsResource StatisticsFromGetAll()
        {
            var result = (Ok<List<MangaResource>>)Subject.GetAll();
            return result.Value.Single().Statistics;
        }

        [Test]
        public void should_link_statistics_from_the_aggregate_service_onto_the_resource()
        {
            GivenStatistics(new MangaStatistics
            {
                MangaId = 4,
                TotalChapterCount = 3,
                ChapterCount = 2,
                ChapterFileCount = 1,
                MonitoredChapterCount = 2,
                SizeOnDisk = 4096,
            });

            var stats = StatisticsFromGetAll();

            stats.TotalChapterCount.Should().Be(3);
            stats.ChapterCount.Should().Be(2);
            stats.ChapterFileCount.Should().Be(1);
            stats.MonitoredChapterCount.Should().Be(2);
            stats.SizeOnDisk.Should().Be(4096);

            // TV-shape alias fields mirror the chapter-shape canonical fields.
            stats.EpisodeCount.Should().Be(2);
            stats.EpisodeFileCount.Should().Be(1);
            stats.TotalEpisodeCount.Should().Be(3);
            stats.MonitoredEpisodeCount.Should().Be(2);
            stats.SeasonCount.Should().Be(0);
        }

        [Test]
        public void should_attach_zeroed_statistics_when_manga_is_absent_from_the_aggregate_query()
        {
            // The repository GROUP BY only emits rows for manga that have chapters. A manga with
            // none (here: the stats list does not contain MangaId 4) must still get a non-null,
            // all-zero Statistics object so the Phase 7 index tiles render "0 / 0" rather than
            // breaking on a missing field (the original F-05 bug).
            GivenStatistics(); // empty — no row for manga 4

            var stats = StatisticsFromGetAll();

            stats.Should().NotBeNull();
            stats.TotalChapterCount.Should().Be(0);
            stats.ChapterCount.Should().Be(0);
            stats.ChapterFileCount.Should().Be(0);
            stats.EpisodeCount.Should().Be(0);
            stats.SeasonCount.Should().Be(0);
        }
    }
}
