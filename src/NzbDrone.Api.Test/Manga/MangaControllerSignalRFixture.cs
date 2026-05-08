using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Mangarr.Api.V5.Manga;
using Mangarr.Http;

namespace NzbDrone.Api.Test.Manga
{
    // Phase 10 Plan 10-05 fixture: covers the 3 new IHandle dispatch paths added to
    // MangaController (MangaEditedEvent / MangaRenamedEvent / MangaBulkEditedEvent →
    // SignalR Updated broadcast). Mirrors the AutoMoqer + IBroadcastSignalRMessage
    // pattern from ChapterControllerFixture (Plan 07-01) + the predicate-locking
    // discipline from MangaMediaCoverServiceFixture (Plan 09-13).
    //
    // Fixture lives under NzbDrone.Api.Test rather than NzbDrone.Core.Test (per the
    // 10-05 plan's nominal path) because Sonarr.Core.Test does not project-reference
    // Mangarr.Api.V5; Sonarr.Api.Test does. Same Mocker / TestBase<TSubject> behaviour;
    // only the project boundary changes. Documented as Rule 3 deviation in the plan
    // SUMMARY.
    //
    // BroadcastResourceChange path (RestControllerWithSignalR.cs:52-67) requires
    // IBroadcastSignalRMessage.IsConnected to be true AND requires the controller's
    // GetResourceById(int) override to return a non-null resource — Setup wires both.
    [TestFixture]
    public class MangaControllerSignalRFixture : TestBase<MangaController>
    {
        private NzbDrone.Core.Manga.Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 42)
                .With(m => m.Title = "Test Manga")
                .With(m => m.Images = new List<MediaCover>())
                .With(m => m.Genres = new List<string>())
                .With(m => m.Tags = new HashSet<int>())
                .Build();

            // RestControllerWithSignalR short-circuits BroadcastMessage when IsConnected
            // is false. Force true so the mock observes the BroadcastMessage call.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(true);

            // BroadcastResourceChange(ModelAction, int) delegates back through
            // GetResourceById -> _mangaService.GetManga(id). Default mock returns null,
            // which would throw NotFoundException; provide the Builder manga instead so
            // the broadcast reaches BroadcastMessage.
            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetManga(It.IsAny<int>()))
                  .Returns(_manga);
        }

        // WR-02 fix helper: build a distinct Manga with the given Id so per-id
        // mock stubs in the bulk-handler tests can return different instances.
        // The bulk tests then bind Verify(...) on Resource.Id == expectedId so
        // a regression that captures only the first id (or always Id=42) fails
        // the assertion. Mirrors the per-id mock pattern from
        // 10-REVIEW.md WR-02 fix exemplar.
        private static NzbDrone.Core.Manga.Manga BuildManga(int id)
        {
            return Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = id)
                .With(m => m.Title = $"Manga {id}")
                .With(m => m.Images = new List<MediaCover>())
                .With(m => m.Genres = new List<string>())
                .With(m => m.Tags = new HashSet<int>())
                .Build();
        }

        // ===================== Plan 10-05 — MangaEditedEvent =====================

        [Test]
        public void Handle_MangaEditedEvent_should_BroadcastResourceChange_Updated_with_manga_id()
        {
            var oldManga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 42)
                .With(m => m.Images = new List<MediaCover>())
                .With(m => m.Genres = new List<string>())
                .With(m => m.Tags = new HashSet<int>())
                .Build();

            Subject.Handle(new MangaEditedEvent(_manga, oldManga));

            // Predicate locked per W6 (revision iteration 1) — verbatim shape extracted
            // from RestControllerWithSignalR.cs:81-89 (SignalRMessage construction confirms
            // Name = Resource = "manga" + Action = ModelAction.Updated):
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Updated && m.Name == "manga")),
                      Times.Once);
        }

        // ===================== Plan 10-05 — MangaRenamedEvent =====================

        [Test]
        public void Handle_MangaRenamedEvent_should_BroadcastResourceChange_Updated_with_manga_id()
        {
            // MangaRenamedEvent ctor: (Manga, List<RenamedChapterFile>); empty list is
            // valid for SignalR test — handler only reads message.Manga.Id.
            Subject.Handle(new MangaRenamedEvent(_manga, new List<RenamedChapterFile>()));

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Updated && m.Name == "manga")),
                      Times.Once);
        }

        // ===================== Plan 10-05 — MangaBulkEditedEvent =====================

        [Test]
        public void Handle_MangaBulkEditedEvent_should_BroadcastResourceChange_Updated_for_each_manga_in_payload()
        {
            // WR-02 fix: stub GetManga per-id with distinct Manga instances so the
            // Verify(...) predicate can bind on Resource.Id == expectedId. The
            // SetUp wildcard `It.IsAny<int>().Returns(_manga)` is overridden by
            // these more-specific Setup(int)`s — Moq picks the most specific
            // matcher per call.
            foreach (var id in new[] { 1, 2, 3 })
            {
                var thisId = id;
                Mocker.GetMock<IMangaService>()
                      .Setup(s => s.GetManga(thisId))
                      .Returns(BuildManga(thisId));
            }

            var bulk = new List<NzbDrone.Core.Manga.Manga>
            {
                BuildManga(1),
                BuildManga(2),
                BuildManga(3),
            };

            Subject.Handle(new MangaBulkEditedEvent(bulk));

            // WR-02 fix: bind the Verify predicate on Resource.Id == expectedId
            // (one Verify per id, with Times.Once). This catches the regression
            // class where the iteration variable is captured incorrectly (e.g.
            // `BroadcastResourceChange(ModelAction.Updated, message.Manga[0].Id)`
            // broadcasting Id=1 three times) which the old same-shape predicate
            // + Times.Exactly(3) silently passed.
            foreach (var id in new[] { 1, 2, 3 })
            {
                var expectedId = id;
                Mocker.GetMock<IBroadcastSignalRMessage>()
                      .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                          m.Action == ModelAction.Updated &&
                          m.Name == "manga" &&
                          ((ResourceChangeMessage<MangaResource>)m.Body).Resource.Id == expectedId)),
                          Times.Once);
            }
        }

        // ===================== Plan 10-06 — ChapterFileAddedEvent =====================

        [Test]
        public void Handle_ChapterFileAddedEvent_should_BroadcastResourceChange_Updated_with_manga_id()
        {
            var chapterFile = Builder<ChapterFile>.CreateNew()
                .With(c => c.MangaId = 42)
                .Build();

            // ChapterFileAddedEvent ctor: (ChapterFile chapterFile) — verified
            // src/NzbDrone.Core/MediaFiles/Events/ChapterFileAddedEvent.cs:13.
            Subject.Handle(new ChapterFileAddedEvent(chapterFile));

            // Predicate locked per W6 (revision iteration 1) — verbatim shape from
            // Plan 10-05 Task 2 exemplar. Resource name "manga" auto-derives from
            // MangaResource type name via RestControllerWithSignalR convention.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Updated && m.Name == "manga")),
                      Times.Once);
        }

        // ===================== Plan 10-06 — ChapterFileDeletedEvent =====================

        [Test]
        public void Handle_ChapterFileDeletedEvent_should_BroadcastResourceChange_Updated_with_manga_id()
        {
            var chapterFile = Builder<ChapterFile>.CreateNew()
                .With(c => c.MangaId = 42)
                .Build();

            // ChapterFileDeletedEvent ctor: (ChapterFile chapterFile, DeleteMediaFileReason reason) —
            // verified src/NzbDrone.Core/MediaFiles/Events/ChapterFileDeletedEvent.cs:12.
            // DeleteMediaFileReason.Manual is the canonical user-deletion reason and bypasses
            // the Upgrade short-circuit in the handler so the broadcast fires.
            Subject.Handle(new ChapterFileDeletedEvent(chapterFile, DeleteMediaFileReason.Manual));

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Updated && m.Name == "manga")),
                      Times.Once);
        }

        // ===================== Plan 10-06 — Upgrade short-circuit =====================

        [Test]
        public void Handle_ChapterFileDeletedEvent_should_NOT_broadcast_when_reason_is_Upgrade()
        {
            var chapterFile = Builder<ChapterFile>.CreateNew()
                .With(c => c.MangaId = 42)
                .Build();

            // Upgrade-reason short-circuit mirrors SeriesController.Handle(EpisodeFileDeletedEvent):
            // the upcoming Add event will fire next, so broadcasting twice for one logical change is
            // wasteful. Verify the short-circuit by asserting BroadcastMessage was NEVER called.
            Subject.Handle(new ChapterFileDeletedEvent(chapterFile, DeleteMediaFileReason.Upgrade));

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.IsAny<SignalRMessage>()), Times.Never);
        }

        // ===================== Plan 10-08 — MangaImportedEvent =====================

        [Test]
        public void Handle_MangaImportedEvent_should_BroadcastResourceChange_Updated_for_each_id_in_payload()
        {
            // MangaImportedEvent ctor: (List<int> mangaIds) — verified
            // src/NzbDrone.Core/Manga/Events/MangaImportedEvent.cs:17-20.
            // Payload is purely numeric (no Manga objects); handler iterates and broadcasts once per id.

            // WR-02 fix: stub GetManga per-id with distinct Manga instances so the
            // Verify(...) predicate can bind on Resource.Id == expectedId (one
            // Verify per id, Times.Once). The SetUp wildcard
            // `It.IsAny<int>().Returns(_manga)` is overridden by these more
            // specific Setup(int)s — Moq picks the most specific matcher per
            // call. Without per-id mocks, every broadcast carries Id=42 and
            // a same-shape Times.Exactly(3) predicate silently passes even if
            // the handler erroneously broadcasts Id=42 three times instead of
            // 1, 2, 3.
            foreach (var id in new[] { 1, 2, 3 })
            {
                var thisId = id;
                Mocker.GetMock<IMangaService>()
                      .Setup(s => s.GetManga(thisId))
                      .Returns(BuildManga(thisId));
            }

            var evt = new MangaImportedEvent(new List<int> { 1, 2, 3 });

            Subject.Handle(evt);

            // WR-02 fix: per-id binding on Resource.Id catches the iteration-
            // variable-captured regression class (broadcast Id=1 three times
            // instead of Ids 1, 2, 3) which the old same-shape Times.Exactly(3)
            // predicate silently passed.
            foreach (var id in new[] { 1, 2, 3 })
            {
                var expectedId = id;
                Mocker.GetMock<IBroadcastSignalRMessage>()
                      .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                          m.Action == ModelAction.Updated &&
                          m.Name == "manga" &&
                          ((ResourceChangeMessage<MangaResource>)m.Body).Resource.Id == expectedId)),
                          Times.Once);
            }
        }
    }
}
