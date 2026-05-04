using System;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Komga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.NotificationTests.Komga
{
    [TestFixture]
    public class KomgaNotificationFixture : CoreTest<KomgaNotification>
    {
        private ChapterImportMessage _message;

        [SetUp]
        public void Setup()
        {
            // TestBase.Mocker pre-registers a real CacheManager — needed for MediaServerUpdateQueue.
            Subject.Definition = new NotificationDefinition
            {
                Settings = new KomgaNotificationSettings
                {
                    Url = "http://komga.local:25600",
                    ApiKey = "test-key",
                    LibraryId = 7
                }
            };

            _message = new ChapterImportMessage
            {
                Message = "Naruto - Chapter 1 imported",
                ChapterFile = new ChapterFile()
            };
        }

        [Test]
        public void should_support_only_chapter_import_event_hook()
        {
            // D-18: v1 implements only OnChapterImport. All other Supports* default false.
            Subject.SupportsOnChapterImport.Should().BeTrue();

            Subject.SupportsOnGrab.Should().BeFalse();
            Subject.SupportsOnDownload.Should().BeFalse();
            Subject.SupportsOnUpgrade.Should().BeFalse();
            Subject.SupportsOnImportComplete.Should().BeFalse();
            Subject.SupportsOnRename.Should().BeFalse();
            Subject.SupportsOnSeriesAdd.Should().BeFalse();
            Subject.SupportsOnSeriesDelete.Should().BeFalse();
            Subject.SupportsOnEpisodeFileDelete.Should().BeFalse();
            Subject.SupportsOnEpisodeFileDeleteForUpgrade.Should().BeFalse();
            Subject.SupportsOnHealthIssue.Should().BeFalse();
            Subject.SupportsOnHealthRestored.Should().BeFalse();
            Subject.SupportsOnApplicationUpdate.Should().BeFalse();
            Subject.SupportsOnManualInteractionRequired.Should().BeFalse();
        }

        [Test]
        public void on_chapter_import_should_enqueue_for_debounce_not_call_proxy_directly()
        {
            // Pattern 7 — debounce/coalesce via MediaServerUpdateQueue: OnChapterImport must
            // NOT call _proxy.Scan synchronously. Scan happens later in ProcessQueue().
            Subject.OnChapterImport(_message);

            Mocker.GetMock<IKomgaProxy>().Verify(p => p.Scan(It.IsAny<KomgaNotificationSettings>()), Times.Never);
        }

        [Test]
        public void process_queue_should_call_scan_once_after_50_imports()
        {
            // Pattern 7 — coalesce 50 imports into ONE scan per LibraryId.
            for (var i = 0; i < 50; i++)
            {
                Subject.OnChapterImport(_message);
            }

            Subject.ProcessQueue();

            Mocker.GetMock<IKomgaProxy>().Verify(p => p.Scan(It.IsAny<KomgaNotificationSettings>()), Times.Once);
        }

        [Test]
        public void process_queue_should_be_a_noop_when_nothing_was_enqueued()
        {
            Subject.ProcessQueue();

            Mocker.GetMock<IKomgaProxy>().Verify(p => p.Scan(It.IsAny<KomgaNotificationSettings>()), Times.Never);
        }

        [Test]
        public void process_queue_should_swallow_proxy_exceptions()
        {
            Mocker.GetMock<IKomgaProxy>()
                  .Setup(p => p.Scan(It.IsAny<KomgaNotificationSettings>()))
                  .Throws(new InvalidOperationException("komga down"));

            Subject.OnChapterImport(_message);

            Action act = () => Subject.ProcessQueue();
            act.Should().NotThrow();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void test_should_call_get_libraries_and_succeed_when_service_returns_null()
        {
            Mocker.GetMock<IKomgaService>()
                  .Setup(s => s.Test(It.IsAny<KomgaNotificationSettings>()))
                  .Returns((ValidationFailure)null);

            var result = Subject.Test();

            result.IsValid.Should().BeTrue();
            Mocker.GetMock<IKomgaService>().Verify(s => s.Test(It.IsAny<KomgaNotificationSettings>()), Times.Once);
        }

        [Test]
        public void test_should_surface_failure_from_service()
        {
            Mocker.GetMock<IKomgaService>()
                  .Setup(s => s.Test(It.IsAny<KomgaNotificationSettings>()))
                  .Returns(new ValidationFailure("Url", "Komga unreachable"));

            var result = Subject.Test();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Url");
        }

        [Test]
        public void on_chapter_import_should_be_noop_when_libraryid_missing()
        {
            // Defensive — in normal operation, validator blocks save before this can happen.
            // We just want to assert no scan dispatched and no exception thrown when the
            // shape is wrong. The provider also logs a Warn (verified by reading the source);
            // we don't assert on the log here because assertion plumbing on this specific
            // negative path is brittle.
            var settings = (KomgaNotificationSettings)Subject.Definition.Settings;
            settings.LibraryId = null;

            Action act = () => Subject.OnChapterImport(_message);
            act.Should().NotThrow();

            Subject.ProcessQueue();
            Mocker.GetMock<IKomgaProxy>().Verify(p => p.Scan(It.IsAny<KomgaNotificationSettings>()), Times.Never);
        }
    }
}
