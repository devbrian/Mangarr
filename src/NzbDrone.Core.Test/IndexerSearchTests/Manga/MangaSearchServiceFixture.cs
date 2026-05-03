using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests.Manga
{
    // Phase 6 Plan 06-06 Task 2 — MangaSearchService dispatch behaviors.
    [TestFixture]
    public class MangaSearchServiceFixture : CoreTest<MangaSearchService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IMangaService>()
                .Setup(m => m.GetManga(It.IsAny<int>()))
                .Returns((int id) => new NzbDrone.Core.Manga.Manga { Id = id, Title = "Test Manga " + id });

            Mocker.GetMock<IChapterService>()
                .Setup(c => c.GetChaptersByManga(It.IsAny<int>()))
                .Returns((int mangaId) => new List<NzbDrone.Core.Manga.Chapter>
                {
                    new() { Id = (mangaId * 100) + 1, MangaId = mangaId, ChapterNumber = 1m, Monitored = true, ChapterFileId = null },
                    new() { Id = (mangaId * 100) + 2, MangaId = mangaId, ChapterNumber = 2m, Monitored = true, ChapterFileId = null }
                });

            Mocker.GetMock<IMangaSearchForReleases>()
                .Setup(s => s.MangaSearch(It.IsAny<MangaSearchCriteria>()))
                .ReturnsAsync(new List<MangaDownloadDecision>());
        }

        [Test]
        public void Execute_with_single_manga_id_invokes_release_search_for_that_manga()
        {
            Subject.Execute(new MangaSearchCommand(new List<int> { 42 }, userInvoked: true));

            Mocker.GetMock<IMangaSearchForReleases>()
                .Verify(s => s.MangaSearch(It.Is<MangaSearchCriteria>(c =>
                    c.Manga.Id == 42 &&
                    c.MonitoredChaptersOnly == true &&
                    c.UserInvokedSearch == true)),
                    Times.Once);
        }

        [Test]
        public void Execute_with_multiple_manga_ids_invokes_release_search_per_id()
        {
            Subject.Execute(new MangaSearchCommand(new List<int> { 1, 2, 3 }));

            Mocker.GetMock<IMangaSearchForReleases>()
                .Verify(s => s.MangaSearch(It.IsAny<MangaSearchCriteria>()), Times.Exactly(3));
        }

        [Test]
        public void Execute_only_passes_monitored_chapters_without_files_into_criteria()
        {
            Mocker.GetMock<IChapterService>()
                .Setup(c => c.GetChaptersByManga(7))
                .Returns(new List<NzbDrone.Core.Manga.Chapter>
                {
                    new() { Id = 1, MangaId = 7, Monitored = true, ChapterFileId = null },     // include
                    new() { Id = 2, MangaId = 7, Monitored = false, ChapterFileId = null },    // exclude (unmonitored)
                    new() { Id = 3, MangaId = 7, Monitored = true, ChapterFileId = 99 }        // exclude (already has file)
                });

            Subject.Execute(new MangaSearchCommand(new List<int> { 7 }));

            Mocker.GetMock<IMangaSearchForReleases>()
                .Verify(s => s.MangaSearch(It.Is<MangaSearchCriteria>(c =>
                    c.Chapters.Count == 1 && c.Chapters.Single().Id == 1)),
                    Times.Once);
        }
    }
}
