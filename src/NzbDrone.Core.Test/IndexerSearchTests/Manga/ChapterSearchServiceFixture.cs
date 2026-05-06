using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests.Manga
{
    // Phase 6 Plan 06-06 Task 2 — ChapterSearchService dispatch behaviors.
    [TestFixture]
    public class ChapterSearchServiceFixture : CoreTest<ChapterSearchService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IMangaService>()
                .Setup(m => m.GetManga(It.IsAny<int>()))
                .Returns((int id) => new NzbDrone.Core.Manga.Manga { Id = id, Title = "Test Manga" });

            Mocker.GetMock<IChapterService>()
                .Setup(c => c.GetChapter(It.IsAny<int>()))
                .Returns((int id) => new NzbDrone.Core.Manga.Chapter
                {
                    Id = id,
                    MangaId = 7,
                    ChapterNumber = id,
                    TranslatedLanguage = "en",
                    Monitored = true
                });

            Mocker.GetMock<IMangaSearchForReleases>()
                .Setup(s => s.ChapterSearch(It.IsAny<ChapterSearchCriteria>()))
                .ReturnsAsync(new List<MangaDownloadDecision>());

            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                .Setup(p => p.ProcessDecisions(It.IsAny<List<MangaDownloadDecision>>()))
                .ReturnsAsync(new ProcessedMangaDecisions(
                    new List<MangaDownloadDecision>(),
                    new List<MangaDownloadDecision>(),
                    new List<MangaDownloadDecision>()));
        }

        [Test]
        public void Execute_iterates_chapter_ids_and_calls_release_search_per_chapter()
        {
            Subject.Execute(new ChapterSearchCommand(new List<int> { 100, 101 }));

            Mocker.GetMock<IMangaSearchForReleases>()
                .Verify(s => s.ChapterSearch(It.IsAny<ChapterSearchCriteria>()), Times.Exactly(2));
        }

        [Test]
        public void Execute_propagates_manual_trigger_into_UserInvokedSearch_flag()
        {
            Subject.Execute(new ChapterSearchCommand(new List<int> { 100 }) { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<IMangaSearchForReleases>()
                .Verify(s => s.ChapterSearch(It.Is<ChapterSearchCriteria>(c => c.UserInvokedSearch == true)),
                    Times.Once);
        }

        [Test]
        public void Execute_default_trigger_marks_search_as_NOT_user_invoked()
        {
            Subject.Execute(new ChapterSearchCommand(new List<int> { 100 }));

            Mocker.GetMock<IMangaSearchForReleases>()
                .Verify(s => s.ChapterSearch(It.Is<ChapterSearchCriteria>(c => c.UserInvokedSearch == false)),
                    Times.Once);
        }
    }
}
