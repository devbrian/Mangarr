using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Sonarr.Api.V5.Manga.Chapter;
using Sonarr.Http.REST;

namespace NzbDrone.Api.Test.Manga.Chapter
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 7 Plan 07-01 D-07.
    // Role-match analog: would mirror an EpisodeControllerFixture if Sonarr shipped one;
    // since it does not, this fixture follows the same AutoMoqer + TestBase<T> + per-test
    // Mocker.SetConstant pattern that Phase 6 controller-adjacent fixtures established
    // (e.g. ChapterServiceFixture).
    //
    // Covers all 6 endpoint shapes wired by ChapterController:
    //   1. GET /api/v5/chapter?mangaId={id}        → IChapterService.GetChaptersByManga
    //   2. GET /api/v5/chapter?chapterIds=...      → IChapterService.GetChapters
    //   3. GET /api/v5/chapter (no params)         → 400 BadRequest (T-07-01)
    //   4. PUT /api/v5/chapter/{id}                → IChapterService.SetChapterMonitored
    //   5. PUT /api/v5/chapter/monitor (1 id)      → single-row path
    //   6. PUT /api/v5/chapter/monitor (>1 ids)    → bulk path (Plan 07-01 Task 1)
    //   7. POST /api/v5/chapter/{id}/search        → IManageCommandQueue.Push(ChapterSearchCommand)
    [TestFixture]
    public class ChapterControllerFixture : TestBase<ChapterController>
    {
        [SetUp]
        public void Setup()
        {
            // RestControllerWithSignalR base requires a non-null IBroadcastSignalRMessage.
            // The fixture only exercises endpoint methods (no Handle calls), so a default
            // Mock<> with IsConnected=false is sufficient — broadcasts short-circuit.
            Mocker.SetConstant<IBroadcastSignalRMessage>(new Mock<IBroadcastSignalRMessage>().Object);
        }

        [Test]
        public void GetChapters_with_mangaId_returns_chapters_filtered_to_manga()
        {
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
                new() { Id = 2, MangaId = 42, ChapterNumber = 2m, Monitored = false, ChapterType = ChapterType.Regular },
            };
            Mocker.GetMock<IChapterService>().Setup(s => s.GetChaptersByManga(42)).Returns(chapters);

            var result = Subject.GetChapters(42, new List<int>());

            // Result is Results<Ok<List<ChapterResource>>, BadRequest> — pull the Ok branch.
            result.Result.Should().BeOfType<Ok<List<ChapterResource>>>();
            var ok = (Ok<List<ChapterResource>>)result.Result;
            ok.Value.Should().HaveCount(2);
            ok.Value.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2 });

            Mocker.GetMock<IChapterService>().Verify(s => s.GetChaptersByManga(42), Times.Once);
            Mocker.GetMock<IChapterService>().Verify(s => s.GetChapters(It.IsAny<IEnumerable<int>>()), Times.Never);
        }

        [Test]
        public void GetChapters_with_chapterIds_returns_specified_chapters()
        {
            var ids = new List<int> { 1, 2, 3 };
            var chapters = ids.Select(i => new NzbDrone.Core.Manga.Chapter
            {
                Id = i,
                MangaId = 1,
                ChapterNumber = i,
                ChapterType = ChapterType.Regular,
            }).ToList();
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChapters(It.Is<IEnumerable<int>>(e => e.SequenceEqual(ids))))
                .Returns(chapters);

            var result = Subject.GetChapters(null, ids);

            result.Result.Should().BeOfType<Ok<List<ChapterResource>>>();
            ((Ok<List<ChapterResource>>)result.Result).Value.Should().HaveCount(3);

            Mocker.GetMock<IChapterService>()
                .Verify(s => s.GetChapters(It.Is<IEnumerable<int>>(e => e.SequenceEqual(ids))), Times.Once);
            Mocker.GetMock<IChapterService>().Verify(s => s.GetChaptersByManga(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void GetChapters_with_no_params_throws_BadRequestException()
        {
            // T-07-01 mitigation: neither mangaId nor chapterIds → 400 (mirrors
            // EpisodeController.GetEpisodes precedent at EpisodeController.cs:52).
            Assert.Throws<BadRequestException>(() => Subject.GetChapters(null, new List<int>()));
        }

        [Test]
        public void GetResourceById_returns_404_when_chapter_missing()
        {
            // BL-01 mirror: explicit NotFoundException so the framework maps to 404.
            Mocker.GetMock<IChapterService>().Setup(s => s.GetChapter(999)).Returns((NzbDrone.Core.Manga.Chapter)null!);

            // GetResourceByIdWithErrorHandler is the public entry on the base class that
            // wraps GetResourceById in a Results<Ok, NotFound>. Hitting it the same way
            // the framework would.
            Assert.Throws<NotFoundException>(() => Subject.GetResourceByIdWithErrorHandler(999));
        }

        [Test]
        public void SetChapterMonitored_invokes_service_and_returns_updated_resource()
        {
            var chapter = new NzbDrone.Core.Manga.Chapter
            {
                Id = 7, MangaId = 1, Monitored = true, ChapterNumber = 1m, ChapterType = ChapterType.Regular,
            };
            Mocker.GetMock<IChapterService>().Setup(s => s.GetChapter(7)).Returns(chapter);

            var result = Subject.SetChapterMonitored(7, new ChapterResource { Id = 7, Monitored = true });

            result.Should().BeOfType<Ok<ChapterResource>>();
            result.Value!.Id.Should().Be(7);
            result.Value.Monitored.Should().BeTrue();

            Mocker.GetMock<IChapterService>().Verify(s => s.SetChapterMonitored(7, true), Times.Once);
        }

        [Test]
        public void SetChaptersMonitored_with_one_id_uses_single_row_path()
        {
            // Mirrors EpisodeController.SetEpisodesMonitored: single id → singular service
            // call (which handles BL-03 cascade-delete null guard internally), bypassing
            // the bulk-overload fan-out.
            var resource = new ChaptersMonitoredResource { ChapterIds = new() { 5 }, Monitored = true };
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChapters(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<NzbDrone.Core.Manga.Chapter>());

            Subject.SetChaptersMonitored(resource);

            Mocker.GetMock<IChapterService>().Verify(s => s.SetChapterMonitored(5, true), Times.Once);
            Mocker.GetMock<IChapterService>()
                .Verify(s => s.SetChaptersMonitored(It.IsAny<IEnumerable<int>>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void SetChaptersMonitored_with_many_ids_uses_bulk_path()
        {
            // Plan 07-01 Task 1 added the bulk overload — Pitfall 4 ordering invariant:
            // the service writes to DB FIRST and publishes one ChapterUpdatedEvent per id
            // LAST. The controller only verifies the dispatch shape; event ordering is
            // covered by ChapterServiceFixture.
            var resource = new ChaptersMonitoredResource { ChapterIds = new() { 1, 2, 3 }, Monitored = false };
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChapters(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<NzbDrone.Core.Manga.Chapter>());

            Subject.SetChaptersMonitored(resource);

            Mocker.GetMock<IChapterService>()
                .Verify(s => s.SetChaptersMonitored(
                    It.Is<IEnumerable<int>>(e => e.SequenceEqual(new[] { 1, 2, 3 })),
                    false),
                    Times.Once);
            Mocker.GetMock<IChapterService>()
                .Verify(s => s.SetChapterMonitored(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void SearchChapter_pushes_ChapterSearchCommand_with_single_id()
        {
            // D-07 + Phase 6 D-12: single-element ChapterIds list shape, dispatched at
            // CommandPriority.Normal with CommandTrigger.Manual. Returns the queued command
            // id so the React UI can poll progress via the existing /api/v5/command surface.
            var queued = new CommandModel { Id = 123 };
            Mocker.GetMock<IManageCommandQueue>()
                .Setup(q => q.Push(
                    It.IsAny<ChapterSearchCommand>(),
                    It.IsAny<CommandPriority>(),
                    It.IsAny<CommandTrigger>()))
                .Returns(queued);

            var result = Subject.SearchChapter(99);

            result.Should().BeOfType<Accepted<int>>();
            result.Value.Should().Be(123);

            Mocker.GetMock<IManageCommandQueue>().Verify(
                q => q.Push(
                    It.Is<ChapterSearchCommand>(c => c.ChapterIds.Count == 1 && c.ChapterIds[0] == 99),
                    CommandPriority.Normal,
                    CommandTrigger.Manual),
                Times.Once);
        }
    }
}
