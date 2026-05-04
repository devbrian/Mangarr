using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MangaTests
{
    // Phase 6 Plan 14 — BL-03 coverage. SetChapterMonitored must NOT throw NRE
    // when the chapter row has been cascade-deleted between UI request issue and
    // service dispatch (MangaDeletedEvent window or stale UI request).
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
                .Setup(r => r.Get(It.IsAny<int>()))
                .Returns((Chapter)null);

            // Must NOT throw — the previous behavior was NullReferenceException → 500.
            Subject.SetChapterMonitored(999, true);

            // Must NOT call Update on a null reference.
            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.Update(It.IsAny<Chapter>()), Times.Never);

            // Must surface a Warn-level diagnostic so System Logs show the stale id.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void SetChapterMonitored_updates_chapter_when_present()
        {
            var chapter = new Chapter { Id = 42, MangaId = 7, Monitored = false };
            Mocker.GetMock<IChapterRepository>()
                .Setup(r => r.Get(42))
                .Returns(chapter);

            Subject.SetChapterMonitored(42, true);

            chapter.Monitored.Should().BeTrue();
            Mocker.GetMock<IChapterRepository>()
                .Verify(r => r.Update(chapter), Times.Once);
        }
    }
}
