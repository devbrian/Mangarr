using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Download.Manga.Builders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.Manga
{
    // Phase 36 Plan 05 Task 2 — MangaDownloadProcessingService contract tests (LOOP-04 / D-04).
    //
    //   IExecute<ProcessMonitoredMangaDownloadsCommand> reads the monitor's registry and:
    //     1. for each ImportPending → MangaCompletedDownloadService.Import
    //     2. for each FailedPending → MangaFailedDownloadService.ProcessFailed
    //     3. an imported, removable download publishes DownloadCanBeRemovedEvent → RemoveItem(deleteData:true)
    //        Times.Once
    //     4. D-04 — the row transitions through State == Importing BEFORE removal; a Completed-but-not-
    //        imported row is NOT removed (never evicted on completion)
    //     5. eviction uses RemoveItem(deleteData:true) on the client, never a manual _diskProvider.DeleteFolder
    //
    //   Contract tests with the Plan 01 shared builders.
    [TestFixture]
    public class MangaDownloadProcessingServiceFixture : CoreTest<MangaDownloadProcessingService>
    {
        private Mock<IDownloadClient> _downloadClient;
        private DownloadClientDefinition _definition;

        [SetUp]
        public void Setup()
        {
            _definition = new DownloadClientDefinition
            {
                Id = 1,
                Name = "InProcess",
                Protocol = DownloadProtocol.Http
            };

            _downloadClient = new Mock<IDownloadClient>();
            _downloadClient.SetupGet(c => c.Definition).Returns(_definition);

            Mocker.GetMock<IDownloadClientFactory>()
                .Setup(f => f.GetAvailableProviders())
                .Returns(new List<IDownloadClient> { _downloadClient.Object });
        }

        private TrackedDownload BuildPending(TrackedDownloadState state)
        {
            var td = new TrackedDownloadBuilder()
                .WithDownloadId("dl-1")
                .WithChapters(42)
                .Completed()
                .WithOutputPath(@"C:\staging\ch001.cbz")
                .Build();

            td.State = state;
            td.DownloadClient = _definition.Id;
            td.IsTrackable = true;
            return td;
        }

        private void RegistryReturns(params TrackedDownload[] downloads)
        {
            Mocker.GetMock<IMangaDownloadMonitoringService>()
                .Setup(m => m.GetTrackedDownloads())
                .Returns(new List<TrackedDownload>(downloads));
        }

        // WR-05: Import now reports whether it actually imported. Default the mock to a genuine import
        // (true) so the existing happy-path tests keep flipping the row to Imported; individual tests
        // override with .Returns(false) to exercise the rejection/short-circuit retention path.
        private void ImportReturns(bool imported)
        {
            Mocker.GetMock<IMangaCompletedDownloadService>()
                .Setup(c => c.Import(It.IsAny<TrackedDownload>()))
                .Returns(imported);
        }

        // ── 1. ImportPending → Import ───────────────────────────────────────────────────

        [Test]
        public void Execute_imports_each_ImportPending_tracked_download()
        {
            var td = BuildPending(TrackedDownloadState.ImportPending);
            RegistryReturns(td);
            ImportReturns(true);

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            Mocker.GetMock<IMangaCompletedDownloadService>()
                .Verify(c => c.Import(td), Times.Once);
        }

        // ── 2. FailedPending → ProcessFailed ────────────────────────────────────────────

        [Test]
        public void Execute_processes_each_FailedPending_tracked_download()
        {
            var td = BuildPending(TrackedDownloadState.FailedPending);
            RegistryReturns(td);

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            Mocker.GetMock<IMangaFailedDownloadService>()
                .Verify(f => f.ProcessFailed(td), Times.Once);
        }

        [Test]
        public void Execute_does_not_import_a_FailedPending_download()
        {
            var td = BuildPending(TrackedDownloadState.FailedPending);
            RegistryReturns(td);

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            Mocker.GetMock<IMangaCompletedDownloadService>()
                .Verify(c => c.Import(It.IsAny<TrackedDownload>()), Times.Never);
        }

        // ── 3 + 5. Imported removable → DownloadCanBeRemovedEvent + RemoveItem(deleteData:true) ──

        [Test]
        public void Execute_evicts_an_imported_removable_download_via_RemoveItem_deleteData_true()
        {
            var td = BuildPending(TrackedDownloadState.ImportPending);

            // The monitor returns the SAME instance across both GetTrackedDownloads() reads (the
            // process loop + RemoveCompletedDownloads). After a GENUINE import the loop flips it to
            // Imported; CanBeRemoved is already true from the Completed() builder.
            RegistryReturns(td);
            ImportReturns(true);

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.Is<DownloadCanBeRemovedEvent>(m => m.TrackedDownload == td)), Times.Once);

            // The kept eviction path: Handle(DownloadCanBeRemovedEvent) → RemoveItem(item, deleteData:true).
            Subject.Handle(new DownloadCanBeRemovedEvent(td));

            _downloadClient.Verify(c => c.RemoveItem(td.DownloadItem, true), Times.Once);
        }

        // ── 4. D-04 — Importing precedes removal; not evicted on completion ─────────────

        [Test]
        public void Import_runs_while_the_row_is_in_state_Importing_D04()
        {
            var td = BuildPending(TrackedDownloadState.ImportPending);
            RegistryReturns(td);

            var stateDuringImport = TrackedDownloadState.ImportPending;
            Mocker.GetMock<IMangaCompletedDownloadService>()
                .Setup(c => c.Import(It.IsAny<TrackedDownload>()))
                .Callback<TrackedDownload>(t => stateDuringImport = t.State)
                .Returns(true);

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            stateDuringImport.Should().Be(TrackedDownloadState.Importing,
                "D-04: the row must pass through Importing during Import — it is never evicted on completion");
            td.State.Should().Be(TrackedDownloadState.Imported,
                "after a successful Import the row reaches Imported (then becomes removable)");
        }

        [Test]
        public void Execute_does_not_evict_a_completed_but_not_yet_imported_download_D04()
        {
            // A Downloading row whose item is Completed+removable but has NOT been imported must NOT
            // be evicted (D-04: never on completion). It is not ImportPending, so the loop skips Import
            // and it never reaches Imported.
            var td = BuildPending(TrackedDownloadState.Downloading);
            RegistryReturns(td);

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>()), Times.Never);
            Mocker.GetMock<IMangaCompletedDownloadService>()
                .Verify(c => c.Import(It.IsAny<TrackedDownload>()), Times.Never);
        }

        // ── WR-05 — rejected/short-circuited import is NOT marked Imported nor evicted ──────────

        [Test]
        public void Execute_does_not_mark_Imported_or_evict_when_import_is_rejected_WR05()
        {
            var td = BuildPending(TrackedDownloadState.ImportPending);
            RegistryReturns(td);

            // Import reports it did NOT import (rejected decision / short-circuit). The row must NOT
            // reach Imported, so RemoveCompletedDownloads must NOT publish a DownloadCanBeRemovedEvent
            // (which would evict the scratch data with deleteData:true and break retry).
            ImportReturns(false);

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            td.State.Should().Be(TrackedDownloadState.ImportPending,
                "WR-05: a rejected/short-circuited import must leave the row in ImportPending for retry, never flip it to Imported");

            Mocker.GetMock<IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>()),
                    Times.Never,
                    "WR-05: a non-imported row must not be evicted with deleteData:true");
        }

        [Test]
        public void Execute_marks_Imported_and_evicts_only_on_genuine_import_WR05()
        {
            var td = BuildPending(TrackedDownloadState.ImportPending);
            RegistryReturns(td);
            ImportReturns(true);

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            td.State.Should().Be(TrackedDownloadState.Imported,
                "WR-05: a genuine import flips the row to Imported");

            Mocker.GetMock<IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.Is<DownloadCanBeRemovedEvent>(m => m.TrackedDownload == td)),
                    Times.Once,
                    "WR-05: a genuinely-imported removable row IS evicted");
        }

        // ── #319 — an exception thrown mid-Import must not strand the row in Importing ─────────

        [Test]
        public void Execute_reverts_row_to_ImportPending_when_Import_throws_319()
        {
            var td = BuildPending(TrackedDownloadState.ImportPending);
            RegistryReturns(td);

            // The exact #318 shape: Import throws (e.g. ModelNotFoundException) AFTER the loop set the
            // row to Importing. Pre-fix, the state-revert never ran and the row was stranded in Importing
            // forever (the monitor only re-drives ImportPending), wedging "Downloaded - Importing".
            Mocker.GetMock<IMangaCompletedDownloadService>()
                .Setup(c => c.Import(It.IsAny<TrackedDownload>()))
                .Throws(new System.InvalidOperationException("boom"));

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            td.State.Should().Be(TrackedDownloadState.ImportPending,
                "#319: a thrown import must revert Importing -> ImportPending so the next poll re-drives it");

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void Execute_warns_the_tracked_download_when_Import_throws_319()
        {
            var td = BuildPending(TrackedDownloadState.ImportPending);
            RegistryReturns(td);

            Mocker.GetMock<IMangaCompletedDownloadService>()
                .Setup(c => c.Import(It.IsAny<TrackedDownload>()))
                .Throws(new System.InvalidOperationException("boom"));

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            td.Status.Should().Be(TrackedDownloadStatus.Warning,
                "#319: a thrown import must surface on the queue row (Status=Warning) instead of failing silently");
            td.StatusMessages.Should().NotBeEmpty();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void Execute_does_not_evict_when_Import_throws_319()
        {
            var td = BuildPending(TrackedDownloadState.ImportPending);
            RegistryReturns(td);

            Mocker.GetMock<IMangaCompletedDownloadService>()
                .Setup(c => c.Import(It.IsAny<TrackedDownload>()))
                .Throws(new System.InvalidOperationException("boom"));

            Subject.Execute(new ProcessMonitoredMangaDownloadsCommand());

            Mocker.GetMock<IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>()),
                    Times.Never,
                    "#319: a row whose import threw must not be evicted (scratch data preserved for retry)");

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
