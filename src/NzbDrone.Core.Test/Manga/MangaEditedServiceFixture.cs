using System;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Phase 32 Plan 32-02 (CORR-02) — MangaEditedService.Handle(MangaEditedEvent)
    // refresh gate + path-change Rescan behavior matrix:
    //   1. path-only edit  → push RescanMangaCommand,                 NOT RefreshMangaCommand
    //   2. ID-only edit    → push RefreshMangaCommand,                NOT RescanMangaCommand
    //   3. combined edit   → push BOTH RescanMangaCommand AND RefreshMangaCommand
    //   4. no-op edit      → push NEITHER command (e.g. Tags-only)
    //
    // Helper BuildManga signature mirrors Manga.cs:29-31 entity shape:
    //   MangaDexId is Guid?, MalId is int?, AniListId is int? — all three nullable.
    //   Path is string. The Guid? typing for MangaDexId is critical (MangaDex IDs are
    //   UUIDs per Phase 2 D-09); a string-typed first parameter would silently compile
    //   against the production gate's != comparison but assert against wrong types.
    [TestFixture]
    public class MangaEditedServiceFixture : CoreTest<MangaEditedService>
    {
        private static readonly Guid MangaDexId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid MangaDexId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

        private static Manga.Manga BuildManga(int id, string path, Guid? mangaDexId, int? malId, int? aniListId)
        {
            return new Manga.Manga
            {
                Id = id,
                Path = path,
                MangaDexId = mangaDexId,
                MalId = malId,
                AniListId = aniListId
            };
        }

        [Test]
        public void Handle_with_path_only_change_pushes_Rescan_but_not_Refresh()
        {
            var oldManga = BuildManga(42, @"C:\old\path", MangaDexId1, 100, 200);
            var newManga = BuildManga(42, @"C:\new\path", MangaDexId1, 100, 200);

            Subject.Handle(new MangaEditedEvent(newManga, oldManga));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(
                      It.Is<RescanMangaCommand>(c => c.MangaId == 42),
                      It.IsAny<CommandPriority>(),
                      It.IsAny<CommandTrigger>()),
                          Times.Once);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(
                      It.IsAny<RefreshMangaCommand>(),
                      It.IsAny<CommandPriority>(),
                      It.IsAny<CommandTrigger>()),
                          Times.Never);
        }

        [Test]
        public void Handle_with_id_only_change_pushes_Refresh_but_not_Rescan()
        {
            var oldManga = BuildManga(42, @"C:\same\path", MangaDexId1, 100, 200);
            var newManga = BuildManga(42, @"C:\same\path", MangaDexId2, 100, 200);

            Subject.Handle(new MangaEditedEvent(newManga, oldManga));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(
                      It.Is<RefreshMangaCommand>(c => c.MangaIds.Contains(42)),
                      It.IsAny<CommandPriority>(),
                      It.IsAny<CommandTrigger>()),
                          Times.Once);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(
                      It.IsAny<RescanMangaCommand>(),
                      It.IsAny<CommandPriority>(),
                      It.IsAny<CommandTrigger>()),
                          Times.Never);
        }

        [Test]
        public void Handle_with_combined_path_and_id_change_pushes_both_Rescan_and_Refresh()
        {
            var oldManga = BuildManga(42, @"C:\old\path", MangaDexId1, 100, 200);
            var newManga = BuildManga(42, @"C:\new\path", MangaDexId1, 999, 200);

            Subject.Handle(new MangaEditedEvent(newManga, oldManga));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(
                      It.Is<RescanMangaCommand>(c => c.MangaId == 42),
                      It.IsAny<CommandPriority>(),
                      It.IsAny<CommandTrigger>()),
                          Times.Once);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(
                      It.Is<RefreshMangaCommand>(c => c.MangaIds.Contains(42)),
                      It.IsAny<CommandPriority>(),
                      It.IsAny<CommandTrigger>()),
                          Times.Once);
        }

        [Test]
        public void Handle_with_no_op_edit_pushes_neither_command()
        {
            var oldManga = BuildManga(42, @"C:\same\path", MangaDexId1, 100, 200);
            var newManga = BuildManga(42, @"C:\same\path", MangaDexId1, 100, 200);

            Subject.Handle(new MangaEditedEvent(newManga, oldManga));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(
                      It.IsAny<RescanMangaCommand>(),
                      It.IsAny<CommandPriority>(),
                      It.IsAny<CommandTrigger>()),
                          Times.Never);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(
                      It.IsAny<RefreshMangaCommand>(),
                      It.IsAny<CommandPriority>(),
                      It.IsAny<CommandTrigger>()),
                          Times.Never);
        }

        [Test]
        [Platform("Win")]
        public void Handle_with_case_only_path_change_on_case_insensitive_fs_pushes_neither_command()
        {
            // WR-01 regression: PathEquals respects OS-level case sensitivity.
            // On Windows / macOS a case-different re-save is a no-op and must not queue Rescan.
            var oldManga = BuildManga(42, @"C:\Manga\Foo", MangaDexId1, 100, 200);
            var newManga = BuildManga(42, @"c:\manga\foo", MangaDexId1, 100, 200);

            Subject.Handle(new MangaEditedEvent(newManga, oldManga));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(
                      It.IsAny<RescanMangaCommand>(),
                      It.IsAny<CommandPriority>(),
                      It.IsAny<CommandTrigger>()),
                          Times.Never);
        }
    }
}
