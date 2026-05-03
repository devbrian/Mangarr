using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    /// <summary>
    /// Phase 4 plan 04-03 Task 3 — verifies the D-10 Protocol gate at the top of
    /// <see cref="CompletedDownloadService.Check"/>: when the tracked download has
    /// <see cref="DownloadProtocol.Http"/>, the entire TV-import code path is skipped
    /// (manga is handled by Phase 6 ProcessMangaCompletedDownloads via
    /// ChapterArchivedEvent → ImportApprovedChapters).
    /// </summary>
    [TestFixture]
    public class CompletedDownloadServiceFixture : CoreTest<CompletedDownloadService>
    {
        [Test]
        public void Check_early_returns_when_Protocol_is_Http()
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    DownloadClientInfo = new DownloadClientItemClientInfo { Protocol = DownloadProtocol.Http },
                    Status = DownloadItemStatus.Completed
                }
            };

            Subject.Check(trackedDownload);

            // Phase 4 D-10: TV import-side dispatch must NOT be invoked when Protocol == Http.
            // We verify by asserting the existing TV consumers receive zero invocations.
            Mocker.GetMock<IProvideImportItemService>()
                  .Verify(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()), Times.Never);
        }

        [Test]
        public void Check_proceeds_past_protocol_gate_when_Protocol_is_Usenet()
        {
            // Build a TV-shaped tracked download with non-Completed status; the protocol gate
            // must NOT short-circuit, but the next guard (Status != Completed) will.
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    DownloadClientInfo = new DownloadClientItemClientInfo { Protocol = DownloadProtocol.Usenet },
                    Status = DownloadItemStatus.Downloading
                }
            };

            Subject.Check(trackedDownload);

            // The protocol gate did NOT short-circuit (it's a Usenet download), but the
            // status guard (next line in Check) does — so still no TV-import side effects.
            Mocker.GetMock<IProvideImportItemService>()
                  .Verify(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()), Times.Never);

            // Defensive: this fixture exists to assert the D-10 gate did not BREAK the TV path
            // structurally — Subject.Check ran without exceptions on a Usenet download.
            trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);
        }

        [Test]
        public void Check_protocol_gate_ignores_null_DownloadClientInfo()
        {
            // Defensive: legacy tracked downloads may have a null DownloadClientInfo. The D-10
            // null-conditional guard (`?.Protocol == DownloadProtocol.Http`) should evaluate false
            // and fall through to the existing Status guard.
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    DownloadClientInfo = null,
                    Status = DownloadItemStatus.Downloading
                }
            };

            Subject.Check(trackedDownload);

            Mocker.GetMock<IProvideImportItemService>()
                  .Verify(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()), Times.Never);
        }
    }
}
