using System;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Kavita;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.NotificationTests.Kavita
{
    [TestFixture]
    public class KavitaNotificationFixture : CoreTest<KavitaNotification>
    {
        private ChapterImportMessage _message;

        [SetUp]
        public void Setup()
        {
            // TestBase.Mocker pre-registers a real CacheManager — needed for MediaServerUpdateQueue.
            Subject.Definition = new NotificationDefinition
            {
                Settings = new KavitaNotificationSettings
                {
                    Url = "http://kavita.local:5000",
                    ApiKey = "test-key",
                    LibraryId = null
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
        }

        [Test]
        public void on_chapter_import_should_enqueue_for_debounce_not_call_proxy_directly()
        {
            // Pattern 7 — debounce/coalesce via MediaServerUpdateQueue: OnChapterImport must
            // NOT call _proxy.Scan synchronously. Scan happens later in ProcessQueue().
            Subject.OnChapterImport(_message);

            Mocker.GetMock<IKavitaProxy>().Verify(p => p.Scan(It.IsAny<KavitaNotificationSettings>()), Times.Never);
        }

        [Test]
        public void process_queue_should_call_scan_once_after_50_imports_for_scan_all()
        {
            // Pattern 7 — coalesce 50 imports into ONE scan-all call (LibraryId = null => sentinel 0).
            for (var i = 0; i < 50; i++)
            {
                Subject.OnChapterImport(_message);
            }

            Subject.ProcessQueue();

            Mocker.GetMock<IKavitaProxy>().Verify(p => p.Scan(It.IsAny<KavitaNotificationSettings>()), Times.Once);
        }

        [Test]
        public void process_queue_should_call_scan_once_per_unique_libraryid()
        {
            // With a fixed Settings.LibraryId, all 50 imports key on the same LibraryId int and
            // collapse to ONE scan call.
            var settings = (KavitaNotificationSettings)Subject.Definition.Settings;
            settings.LibraryId = 7;

            for (var i = 0; i < 50; i++)
            {
                Subject.OnChapterImport(_message);
            }

            Subject.ProcessQueue();

            Mocker.GetMock<IKavitaProxy>().Verify(p => p.Scan(It.IsAny<KavitaNotificationSettings>()), Times.Once);
        }

        [Test]
        public void process_queue_should_be_a_noop_when_nothing_was_enqueued()
        {
            Subject.ProcessQueue();

            Mocker.GetMock<IKavitaProxy>().Verify(p => p.Scan(It.IsAny<KavitaNotificationSettings>()), Times.Never);
        }

        [Test]
        public void process_queue_should_swallow_proxy_exceptions()
        {
            Mocker.GetMock<IKavitaProxy>()
                  .Setup(p => p.Scan(It.IsAny<KavitaNotificationSettings>()))
                  .Throws(new InvalidOperationException("kavita down"));

            Subject.OnChapterImport(_message);

            Action act = () => Subject.ProcessQueue();
            act.Should().NotThrow();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void test_should_succeed_when_service_returns_null()
        {
            Mocker.GetMock<IKavitaService>()
                  .Setup(s => s.Test(It.IsAny<KavitaNotificationSettings>()))
                  .Returns((ValidationFailure)null);

            var result = Subject.Test();

            result.IsValid.Should().BeTrue();
            Mocker.GetMock<IKavitaService>().Verify(s => s.Test(It.IsAny<KavitaNotificationSettings>()), Times.Once);
        }

        [Test]
        public void test_should_surface_failure_from_service()
        {
            Mocker.GetMock<IKavitaService>()
                  .Setup(s => s.Test(It.IsAny<KavitaNotificationSettings>()))
                  .Returns(new ValidationFailure("Url", "Kavita unreachable"));

            var result = Subject.Test();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Url");
        }
    }
}
