using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 plan 04-08 Task 1 — exercises the daily housekeeper's two responsibilities:
    /// (a) DeleteOrphans retention sweep (D-08); (b) orphan scratch dir cleanup keyed on row Id.
    /// </summary>
    [TestFixture]
    public class ChapterDownloadHousekeeperFixture : CoreTest<ChapterDownloadHousekeeper>
    {
        private const string ScratchRoot = "/tmp/scratch-test";

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.DownloadScratchPath).Returns(ScratchRoot);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(ScratchRoot)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetDirectories(ScratchRoot))
                  .Returns(Array.Empty<string>());
            Mocker.GetMock<IChapterDownloadStateRepository>().Setup(r => r.All())
                  .Returns(Array.Empty<ChapterDownloadState>());
        }

        [Test]
        public void Execute_calls_DeleteOrphans_with_now_cutoff()
        {
            Subject.Execute(new HousekeepInProcessDownloadsCommand());

            Mocker.GetMock<IChapterDownloadStateRepository>()
                  .Verify(r => r.DeleteOrphans(It.Is<DateTime>(dt => dt <= DateTime.UtcNow.AddSeconds(1)
                                                                  && dt >= DateTime.UtcNow.AddMinutes(-1))),
                          Times.Once);
        }

        [Test]
        public void Execute_deletes_orphan_scratch_dirs_with_no_DB_row()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetDirectories(ScratchRoot))
                  .Returns(new[] { "/tmp/scratch-test/1", "/tmp/scratch-test/99" });
            Mocker.GetMock<IChapterDownloadStateRepository>().Setup(r => r.All())
                  .Returns(new List<ChapterDownloadState> { new ChapterDownloadState { Id = 1 } });

            Subject.Execute(new HousekeepInProcessDownloadsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder("/tmp/scratch-test/99", true), Times.Once);
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder("/tmp/scratch-test/1", true), Times.Never);
        }

        [Test]
        public void Execute_swallows_DeleteOrphans_exceptions_and_continues_to_scratch_sweep()
        {
            Mocker.GetMock<IChapterDownloadStateRepository>()
                  .Setup(r => r.DeleteOrphans(It.IsAny<DateTime>()))
                  .Throws<InvalidOperationException>();

            Subject.Execute(new HousekeepInProcessDownloadsCommand());

            // Scratch sweep still ran (GetDirectories invoked).
            Mocker.GetMock<IDiskProvider>().Verify(d => d.GetDirectories(ScratchRoot), Times.Once);
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void Execute_skips_scratch_sweep_when_root_does_not_exist()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns(false);

            Subject.Execute(new HousekeepInProcessDownloadsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.GetDirectories(It.IsAny<string>()), Times.Never);
        }
    }
}
