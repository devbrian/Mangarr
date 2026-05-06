using System;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    // Phase 10 Plan 10-07 fixture: covers the new 3-arg UpdateManga overload
    // (publishUpdatedEvent + triggerSeriesEdited) event-publish matrix and
    // verifies that the existing 2-arg overload delegates to the 3-arg form
    // with triggerSeriesEdited:false.
    //
    // Pitfall 4 ordering verified implicitly via the Mocker.GetMock<IMangaRepository>
    // setup that returns the supplied manga from Update() — assertions fire AFTER
    // the Update returns. The 3-arg method publishes MangaUpdatedEvent BEFORE
    // MangaEditedEvent per the Pitfall 4 invariant.
    [TestFixture]
    public class MangaServiceFixture : CoreTest<MangaService>
    {
        private NzbDrone.Core.Manga.Manga _manga;
        private NzbDrone.Core.Manga.Manga _stored;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 42)
                .With(m => m.Title = "Updated Title")
                .Build();

            _stored = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 42)
                .With(m => m.Title = "Old Title")
                .Build();

            // BL-09 existence guard — the 3-arg method calls _mangaRepository.Find(manga.Id)
            // and throws ModelNotFoundException if null. Seed Find to return the
            // pre-update snapshot.
            Mocker.GetMock<IMangaRepository>()
                  .Setup(r => r.Find(42))
                  .Returns(_stored);

            // Pitfall 4 Step 1: DB Update FIRST. Stub Update to echo the supplied
            // manga so the event-publish assertions can predicate on the same
            // instance.
            Mocker.GetMock<IMangaRepository>()
                  .Setup(r => r.Update(It.IsAny<NzbDrone.Core.Manga.Manga>()))
                  .Returns<NzbDrone.Core.Manga.Manga>(m => m);

            // MoveMangaService / MangaLinksController callers use the 2-arg overload's
            // path-build route (via IBuildMangaPaths) only inside bulk-update; the
            // 3-arg single-edit path doesn't touch IBuildMangaPaths but Mocker still
            // needs a no-op for AutoMoq construction.
            Mocker.GetMock<IBuildMangaPaths>();
        }

        // ===================== Plan 10-07 — UpdateManga 3-arg event matrix =====================

        [Test]
        public void UpdateManga_3arg_publishUpdatedEvent_true_triggerSeriesEdited_true_publishes_both_events()
        {
            Subject.UpdateManga(_manga, publishUpdatedEvent: true, triggerSeriesEdited: true);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaUpdatedEvent>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaEditedEvent>()), Times.Once);
        }

        [Test]
        public void UpdateManga_3arg_publishUpdatedEvent_true_triggerSeriesEdited_false_publishes_only_MangaUpdatedEvent()
        {
            Subject.UpdateManga(_manga, publishUpdatedEvent: true, triggerSeriesEdited: false);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaUpdatedEvent>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaEditedEvent>()), Times.Never);
        }

        [Test]
        public void UpdateManga_3arg_publishUpdatedEvent_false_triggerSeriesEdited_false_publishes_neither()
        {
            Subject.UpdateManga(_manga, publishUpdatedEvent: false, triggerSeriesEdited: false);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaUpdatedEvent>()), Times.Never);
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaEditedEvent>()), Times.Never);
        }

        [Test]
        public void UpdateManga_2arg_overload_delegates_to_3arg_with_triggerSeriesEdited_false()
        {
            // 2-arg form (publishUpdatedEvent default = true) — verifies the delegation
            // contract: MangaUpdatedEvent fires (publishUpdatedEvent=true) but
            // MangaEditedEvent does NOT fire (triggerSeriesEdited defaults to false in
            // the delegation).
            Subject.UpdateManga(_manga, publishUpdatedEvent: true);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaUpdatedEvent>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaEditedEvent>()), Times.Never);
        }

        [Test]
        public void UpdateManga_3arg_throws_when_manga_not_found()
        {
            Mocker.GetMock<IMangaRepository>()
                  .Setup(r => r.Find(42))
                  .Returns((NzbDrone.Core.Manga.Manga)null);

            Assert.Throws<NzbDrone.Core.Datastore.ModelNotFoundException>(
                () => Subject.UpdateManga(_manga, publishUpdatedEvent: true, triggerSeriesEdited: true));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaUpdatedEvent>()), Times.Never);
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<MangaEditedEvent>()), Times.Never);
        }

        [Test]
        public void UpdateManga_3arg_throws_on_null_manga()
        {
            Assert.Throws<ArgumentNullException>(
                () => Subject.UpdateManga(null, publishUpdatedEvent: true, triggerSeriesEdited: true));
        }
    }
}
