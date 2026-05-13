using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MangaTests
{
    // issue #81 fixture: covers MoveMangaService (IExecute<MoveMangaCommand> +
    // IExecute<BulkMoveMangaCommand>). 1:1 port of upstream
    // src/NzbDrone.Core.Test/TvTests/MoveSeriesServiceFixture.cs with manga
    // type swaps + an additional idempotency test (no upstream peer) for the
    // sourcePath.PathEquals(destinationPath) short-circuit at
    // MoveMangaService.cs:68-72.
    //
    // The service mirrors upstream MoveSeriesService 1:1 — these tests pin
    // the canonical-translation contract so a future drift surfaces here
    // before users see broken moves.
    [TestFixture]
    public class MoveMangaServiceFixture : CoreTest<MoveMangaService>
    {
        private NzbDrone.Core.Manga.Manga _manga;
        private MoveMangaCommand _command;
        private BulkMoveMangaCommand _bulkCommand;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<NzbDrone.Core.Manga.Manga>
                .CreateNew()
                .Build();

            _command = new MoveMangaCommand
            {
                MangaId = 1,
                SourcePath = @"C:\Test\Manga\Title".AsOsAgnostic(),
                DestinationPath = @"C:\Test\Manga2\Title".AsOsAgnostic()
            };

            _bulkCommand = new BulkMoveMangaCommand
            {
                Manga = new List<BulkMoveManga>
                {
                    new BulkMoveManga
                    {
                        MangaId = 1,
                        SourcePath = @"C:\Test\Manga\Title".AsOsAgnostic()
                    }
                },
                DestinationRootFolder = @"C:\Test\Manga2".AsOsAgnostic()
            };

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetManga(It.IsAny<int>()))
                  .Returns(_manga);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(true);
        }

        private void GivenFailedMove()
        {
            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFolder(It.IsAny<string>(), It.IsAny<string>(), TransferMode.Move))
                  .Throws<IOException>();
        }

        [Test]
        public void should_log_error_when_move_throws_an_exception()
        {
            GivenFailedMove();

            Subject.Execute(_command);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_revert_manga_path_on_error()
        {
            GivenFailedMove();

            Subject.Execute(_command);

            ExceptionVerification.ExpectedErrors(1);

            // RevertPath calls IMangaService.UpdateManga(manga) — the 1-arg
            // overload routes to the existing IMangaService.UpdateManga(Manga,
            // bool publishUpdatedEvent = true) signature (Phase 10 Plan 10-07
            // 2-arg form), which itself delegates to the 3-arg form. The
            // assertion is on either signature being invoked once on revert.
            Mocker.GetMock<IMangaService>()
                  .Verify(v => v.UpdateManga(It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<bool>()), Times.Once());
        }

        [Test]
        public void should_use_destination_path()
        {
            Subject.Execute(_command);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFolder(_command.SourcePath, _command.DestinationPath, TransferMode.Move), Times.Once());

            // Single-manga MoveMangaCommand does NOT call GetMangaFolder — the
            // controller already computed DestinationPath via RootFolderModal +
            // MangaFolderController. Only the bulk path calls GetMangaFolder.
            Mocker.GetMock<IBuildMangaFileNames>()
                  .Verify(v => v.GetMangaFolder(It.IsAny<NzbDrone.Core.Manga.Manga>(), null), Times.Never());
        }

        [Test]
        public void should_build_new_path_when_root_folder_is_provided()
        {
            var mangaFolder = "Title";
            var expectedPath = Path.Combine(_bulkCommand.DestinationRootFolder, mangaFolder);

            Mocker.GetMock<IBuildMangaFileNames>()
                  .Setup(s => s.GetMangaFolder(It.IsAny<NzbDrone.Core.Manga.Manga>(), null))
                  .Returns(mangaFolder);

            Subject.Execute(_bulkCommand);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFolder(_bulkCommand.Manga.First().SourcePath, expectedPath, TransferMode.Move), Times.Once());
        }

        [Test]
        public void should_skip_manga_folder_if_it_does_not_exist()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.Execute(_command);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFolder(_command.SourcePath, _command.DestinationPath, TransferMode.Move), Times.Never());

            Mocker.GetMock<IBuildMangaFileNames>()
                  .Verify(v => v.GetMangaFolder(It.IsAny<NzbDrone.Core.Manga.Manga>(), null), Times.Never());
        }

        [Test]
        public void should_skip_move_when_source_equals_destination_idempotency()
        {
            // Mangarr-specific test (no upstream peer in MoveSeriesServiceFixture):
            // verifies the sourcePath.PathEquals(destinationPath) short-circuit
            // at MoveMangaService.cs:68-72 — re-triggering a Move against the
            // same destination is a logged no-op (no exception, no file
            // operations, no event publish). This is the contract the issue #81
            // smoke-test exercises on the "rerun against same destination" gate.
            var idempotentCommand = new MoveMangaCommand
            {
                MangaId = 1,
                SourcePath = @"C:\Test\Manga\Title".AsOsAgnostic(),
                DestinationPath = @"C:\Test\Manga\Title".AsOsAgnostic()
            };

            Subject.Execute(idempotentCommand);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFolder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TransferMode>()), Times.Never());
        }
    }
}
