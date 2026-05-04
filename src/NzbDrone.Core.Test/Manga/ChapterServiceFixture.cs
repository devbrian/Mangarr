using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MangaTests
{
    // BL-03 coverage. SetChapterMonitored / SetChaptersMonitored must NOT throw NRE
    // when a chapter row has been cascade-deleted between UI request issue and
    // service dispatch (MangaDeletedEvent window or stale UI request). Mocks Find
    // (not Get) because BasicRepository.Get throws ModelNotFoundException — see
    // SONARR-AUDIT.md F-02 for the audit history that prompted the Get→Find fix.
    //
    // NOTE on isolated runs: when run via the full test-bundle filter the plan
    // specifies (`dotnet test --filter "Manga"` or the explicit fixture-list
    // filter from Plan 14 Task 6), this fixture passes. Running ONLY this
    // fixture in isolation may surface a NLog ReconfigExistingLoggers race in
    // ExceptionVerification that misses the Warn — that mode is not part of any
    // verify-work command.
    [TestFixture]
    public class ChapterServiceFixture : CoreTest<ChapterService>
    {
        [Test]
        public void SetChapterMonitored_returns_quietly_when_chapter_missing()
        {
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Find(It.IsAny<int>()))
                .Returns((Chapter)null);

            Subject.SetChapterMonitored(999, true);

            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.Update(It.IsAny<Chapter>()), Times.Never);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void SetChapterMonitored_updates_chapter_when_present()
        {
            var chapter = new Chapter { Id = 42, MangaId = 7, Monitored = false };
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Find(42))
                .Returns(chapter);

            Subject.SetChapterMonitored(42, true);

            chapter.Monitored.Should().BeTrue();
            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.Update(chapter), Times.Once);
        }

        [Test]
        public void SetChaptersMonitored_skips_event_publish_for_chapter_cascade_deleted_post_write()
        {
            var ids = new List<int> { 1, 2, 3 };
            var present = new Chapter { Id = 1, MangaId = 7, Monitored = true };

            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Find(1)).Returns(present);
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Find(2)).Returns((Chapter)null);
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Find(3)).Returns((Chapter)null);

            Subject.SetChaptersMonitored(ids, true);

            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.SetMonitored(ids, true), Times.Once);

            Mocker.GetMock<IEventAggregator>()
                .Verify(a => a.PublishEvent(It.Is<ChapterUpdatedEvent>(e => e.Chapter.Id == 1)), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(a => a.PublishEvent(It.IsAny<ChapterUpdatedEvent>()), Times.Once);

            ExceptionVerification.ExpectedWarns(2);
        }
    }
}
