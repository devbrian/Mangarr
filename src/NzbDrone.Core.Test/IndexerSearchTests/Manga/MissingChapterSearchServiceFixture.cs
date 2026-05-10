using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests.Manga
{
    // Phase 6 Plan 06-06 Task 3 — MissingChapterSearchService walking + group-by-MangaId.
    [TestFixture]
    public class MissingChapterSearchServiceFixture : CoreTest<MissingChapterSearchService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IMangaQueueService>()
                .Setup(q => q.GetMangaQueue())
                .Returns(new List<MangaQueueItem>());

            Mocker.GetMock<IChapterService>()
                .Setup(c => c.AllMissingMonitoredChapters())
                .Returns(new List<NzbDrone.Core.Manga.Chapter>());
        }

        // Phase 16 STRUCT-03: IsSynthetic property removed from Chapter. Phase 16.1
        // Sonarr-canonical Wanted/Missing predicate is `monitored && ChapterFileId == null`
        // (mirrors Episode.Monitored && EpisodeFileId == 0). The `synthetic` arg here is
        // preserved on the factory for legacy test signatures but is now a no-op — synthetic-
        // vs-real distinction collapses into "no file" under the canonical predicate.
        private NzbDrone.Core.Manga.Chapter Ch(int id, int mangaId, bool synthetic = false, bool monitored = true)
        {
            _ = synthetic;
            return new()
            {
                Id = id,
                MangaId = mangaId,
                ChapterNumber = id,
                Monitored = monitored,
                ChapterFileId = null
            };
        }

        [Test]
        public void Execute_with_null_MangaId_walks_AllMissingMonitoredChapters_and_pushes_one_command_per_manga()
        {
            Mocker.GetMock<IChapterService>()
                .Setup(c => c.AllMissingMonitoredChapters())
                .Returns(new List<NzbDrone.Core.Manga.Chapter>
                {
                    Ch(1, 7),
                    Ch(2, 7),
                    Ch(3, 9)
                });

            Subject.Execute(new MissingChapterSearchCommand());

            // D-09 — group-by-MangaId; one MangaSearchCommand per affected Manga.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.Is<MangaSearchCommand>(c => c.MangaIds.Single() == 7),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.Is<MangaSearchCommand>(c => c.MangaIds.Single() == 9),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);
        }

        [Test]
        public void Execute_with_single_manga_id_only_processes_that_manga()
        {
            Mocker.GetMock<IMangaService>()
                .Setup(m => m.GetManga(42))
                .Returns(new NzbDrone.Core.Manga.Manga { Id = 42 });

            Mocker.GetMock<IChapterService>()
                .Setup(c => c.GetChaptersByManga(42))
                .Returns(new List<NzbDrone.Core.Manga.Chapter> { Ch(100, 42) });

            Subject.Execute(new MissingChapterSearchCommand(42));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.Is<MangaSearchCommand>(c => c.MangaIds.Single() == 42),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);

            Mocker.GetMock<IChapterService>()
                .Verify(c => c.AllMissingMonitoredChapters(), Times.Never);
        }

        [Test]
        public void Execute_skips_chapters_already_in_queue()
        {
            Mocker.GetMock<IChapterService>()
                .Setup(c => c.AllMissingMonitoredChapters())
                .Returns(new List<NzbDrone.Core.Manga.Chapter> { Ch(1, 7) });

            // Queued release contains chapter id 1 — must be skipped.
            Mocker.GetMock<IMangaQueueService>()
                .Setup(q => q.GetMangaQueue())
                .Returns(new List<MangaQueueItem>
                {
                    new()
                    {
                        Id = 99,
                        MangaId = 7,
                        ChapterId = 1,
                        RemoteChapter = new RemoteChapter
                        {
                            Manga = new NzbDrone.Core.Manga.Manga { Id = 7 },
                            Chapters = new List<NzbDrone.Core.Manga.Chapter> { Ch(1, 7) }
                        }
                    }
                });

            Subject.Execute(new MissingChapterSearchCommand());

            // No MangaSearchCommand pushed — only chapter was deduped against the queue.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.IsAny<MangaSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);
        }

        [Test]
        public void Execute_includes_synthetic_chapters_in_grouping_per_D_04()
        {
            // D-04: IsSynthetic=true rows are NOT filtered; both rows must be considered
            // and the manga must surface ONE MangaSearchCommand (group-by-MangaId).
            Mocker.GetMock<IChapterService>()
                .Setup(c => c.AllMissingMonitoredChapters())
                .Returns(new List<NzbDrone.Core.Manga.Chapter>
                {
                    Ch(1, 42, synthetic: true),
                    Ch(2, 42, synthetic: false)
                });

            Subject.Execute(new MissingChapterSearchCommand());

            // ONE command per Manga regardless of synthetic flag.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.Is<MangaSearchCommand>(c => c.MangaIds.Count == 1 && c.MangaIds[0] == 42),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);
        }

        [Test]
        public void Execute_propagates_manual_trigger_into_user_invoked_search_flag()
        {
            Mocker.GetMock<IChapterService>()
                .Setup(c => c.AllMissingMonitoredChapters())
                .Returns(new List<NzbDrone.Core.Manga.Chapter> { Ch(1, 7) });

            Subject.Execute(new MissingChapterSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.Is<MangaSearchCommand>(c => c.UserInvokedSearch == true),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);
        }
    }
}
