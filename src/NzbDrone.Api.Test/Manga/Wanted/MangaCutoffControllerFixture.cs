using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Sonarr.Api.V5.Manga.Wanted;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace NzbDrone.Api.Test.Manga.Wanted
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 12 Plan 12-12 (sub-wave-B-addition #4 —
    // F-CUTOFF closure). Phase-12 follow-up (F-CUTOFF-SIGNALR closure, 2026-05-06) extended the
    // fixture with: a base-class assertion, a SetUp method providing the IBroadcastSignalRMessage +
    // IChapterService.GetChapter mocks needed by the BroadcastResourceChange round-trip, and 4 new
    // tests covering the IHandle<ChapterGrabbedEvent> / IHandle<ChapterImportedEvent> /
    // IHandle<ChapterFileDeletedEvent> + Upgrade-reason short-circuit subscriptions (mirrors
    // MangaControllerSignalRFixture pattern at src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs).
    //
    // Role-match analog: src/NzbDrone.Api.Test/Manga/Chapter/ChapterControllerFixture.cs (Plan 07-01 D-07 —
    // canonical AutoMoqer + TestBase<TController> pattern for V5 controller-shape tests) +
    // src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs (Plan 10-05/10-06 — IBroadcastSignalRMessage
    // + GetResourceById round-trip Setup pattern for SignalR-broadcasting controllers).
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test does not
    // project-reference Sonarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs (Plan 10-05 Rule 3 deviation —
    // documented in src/Sonarr.Api.V5/Manga/CLAUDE.md lines 108-111).
    //
    // Per-plan unit-test filter (D-12-20): dotnet test --filter "FullyQualifiedName~MangaCutoff" must
    // return at least 1 passing test. This fixture provides:
    //   1. Happy-path paged GET delegates to IChapterCutoffService.ChaptersWhereCutoffUnmet exactly once.
    //   2. Reflective Attribute lookup confirms route literal "manga/wanted/cutoff" — the contract that
    //      the frontend Plan 12-08 fetch path expects per Plan 07-02 URL-shaped key contract.
    //   3. Filter-expression behavior tests (Phase 12 REVIEW MED-01 defense-in-depth).
    //   4. Subresource hydration tests (Phase 12 REVIEW MED-01 defense-in-depth).
    //   5. (NEW — F-CUTOFF-SIGNALR) Base-class assertion: MangaCutoffController extends
    //      RestControllerWithSignalR<MangaCutoffResource, Chapter> (NOT plain Controller). This
    //      pin protects against silent regression to the plain-Controller shape.
    //   6. (NEW — F-CUTOFF-SIGNALR) IHandle<ChapterGrabbedEvent> broadcasts Updated for each chapter
    //      id in the RemoteChapter.Chapters list.
    //   7. (NEW — F-CUTOFF-SIGNALR) IHandle<ChapterImportedEvent> broadcasts Updated for the imported
    //      chapter's id (single Chapter per event — Plan 06-07 Pitfall 4 ordering).
    //   8. (NEW — F-CUTOFF-SIGNALR) IHandle<ChapterFileDeletedEvent> broadcasts Updated for the
    //      deleted file's ChapterId.
    //   9. (NEW — F-CUTOFF-SIGNALR) IHandle<ChapterFileDeletedEvent> Upgrade-reason short-circuit
    //      mirrors MangaController precedent (no broadcast on Upgrade — the upcoming Add fires next).
    //
    // BroadcastResourceChange path (RestControllerWithSignalR.cs:52-67) requires
    // IBroadcastSignalRMessage.IsConnected to be true AND requires the controller's
    // GetResourceById(int) override to return a non-null resource — SetUp wires both.
    [TestFixture]
    public class MangaCutoffControllerFixture : TestBase<MangaCutoffController>
    {
        [SetUp]
        public void Setup()
        {
            // RestControllerWithSignalR short-circuits BroadcastMessage when IsConnected
            // is false. Force true so the Mocker observes the BroadcastMessage call in
            // the IHandle<...> tests below. Existing happy-path / filter / hydration tests
            // do not exercise the broadcast path, so this SetUp is a no-op for them.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(true);

            // BroadcastResourceChange(ModelAction, int) round-trips through
            // GetResourceById -> _chapterService.GetChapter(id). Default mock returns null,
            // which would NRE when ToCutoffResource() runs. Provide a default non-null
            // chapter for any id so the broadcast reaches BroadcastMessage. Per-test
            // Setups can override with .Setup(s => s.GetChapter(specificId)) for
            // id-binding assertions.
            Mocker.GetMock<IChapterService>()
                  .Setup(s => s.GetChapter(It.IsAny<int>()))
                  .Returns<int>(id => new NzbDrone.Core.Manga.Chapter
                  {
                      Id = id,
                      MangaId = 42,
                      ChapterNumber = 1m,
                      Monitored = true,
                      ChapterType = ChapterType.Regular
                  });
        }

        [Test]
        public void GetCutoffUnmetChapters_delegates_to_chapter_cutoff_service()
        {
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
                new() { Id = 2, MangaId = 42, ChapterNumber = 2m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            var result = Subject.GetCutoffUnmetChapters(new PagingRequestResource());

            result.Should().BeOfType<Ok<PagingResource<MangaCutoffResource>>>();
            var ok = (Ok<PagingResource<MangaCutoffResource>>)result;
            ok.Value!.Records.Should().HaveCount(2);

            Mocker.GetMock<IChapterCutoffService>()
                .Verify(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()),
                    Times.Once);
        }

        [Test]
        public void Route_attribute_is_manga_wanted_cutoff_literal_per_plan_07_02_contract()
        {
            // Plan 07-02 URL-shaped React Query key contract — the route attribute string is the
            // load-bearing contract between the frontend useCutoffUnmet hook (Plan 12-08) and this
            // controller. Mismatched route literal silently breaks every fetch from
            // /manga/wanted/cutoffunmet (the F-CUTOFF symptom Plan 12-99 Task 7 surfaced before
            // Plan 12-12 shipped). Pitfall 5 — TV/manga cache MUST NOT collide.
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(MangaCutoffController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr.Resource.Should().Be("manga/wanted/cutoff");
        }

        // Phase 12 REVIEW MED-01 — defense-in-depth tests asserting the four observable
        // controller behaviors the route-attribute + happy-path tests do not exercise:
        //   * `monitored=true` adds a `c.Monitored == true` FilterExpression
        //   * `mangaIds[]` (non-empty) adds a `mangaIds.Contains(c.MangaId)` FilterExpression
        //   * `includeManga=true` triggers IMangaService.GetManga(MangaId) hydration in MapToResource
        //   * `includeManga=true` populates MangaCutoffResource.Manga with {Id, Title}
        // These pin the FilterExpressions chain + subresource hydration so a future Phase 15
        // collapse / schema change cannot silently strip filter wiring while the smoke test
        // stays green (Phase 11 CR-02 lesson — mocks must verify against the real contract path,
        // not just structural existence).

        [Test]
        public void GetCutoffUnmetChapters_applies_monitored_filter_when_monitored_true()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            Subject.GetCutoffUnmetChapters(new PagingRequestResource(), monitored: true);

            capturedSpec.Should().NotBeNull();
            capturedSpec!.FilterExpressions.Should().NotBeEmpty();

            // Compile + invoke the captured filter expressions against synthetic Chapter rows
            // to verify the monitored filter actually selects only Monitored == true rows.
            var monitoredChapter = new NzbDrone.Core.Manga.Chapter { Monitored = true };
            var unmonitoredChapter = new NzbDrone.Core.Manga.Chapter { Monitored = false };

            var allFiltersPass = capturedSpec.FilterExpressions
                .Select(e => e.Compile())
                .ToList();

            allFiltersPass.All(f => f(monitoredChapter)).Should().BeTrue(
                "monitored=true filter should accept Monitored chapter rows");
            allFiltersPass.Any(f => !f(unmonitoredChapter)).Should().BeTrue(
                "monitored=true filter should reject Unmonitored chapter rows");
        }

        [Test]
        public void GetCutoffUnmetChapters_omits_monitored_filter_when_monitored_false()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            // monitored=false short-circuits the `if (monitored)` branch — no Monitored
            // FilterExpression is added, so unmonitored chapters surface in the result.
            Subject.GetCutoffUnmetChapters(new PagingRequestResource(), monitored: false);

            capturedSpec.Should().NotBeNull();

            // With no other filters supplied, FilterExpressions must be empty.
            capturedSpec!.FilterExpressions.Should().BeEmpty(
                "monitored=false should not add any FilterExpression");
        }

        [Test]
        public void GetCutoffUnmetChapters_applies_mangaIds_filter_when_mangaIds_non_empty()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            // monitored=false to isolate the mangaIds filter.
            Subject.GetCutoffUnmetChapters(new PagingRequestResource(), monitored: false, mangaIds: new[] { 42, 99 });

            capturedSpec.Should().NotBeNull();
            capturedSpec!.FilterExpressions.Should().HaveCount(1,
                "mangaIds filter should add exactly one FilterExpression when monitored=false");

            // Compile + invoke the captured mangaIds filter against synthetic Chapter rows.
            var matchingChapter = new NzbDrone.Core.Manga.Chapter { MangaId = 42 };
            var nonMatchingChapter = new NzbDrone.Core.Manga.Chapter { MangaId = 7 };

            var compiledFilter = capturedSpec.FilterExpressions[0].Compile();
            compiledFilter(matchingChapter).Should().BeTrue(
                "mangaIds filter should accept rows whose MangaId is in the supplied list");
            compiledFilter(nonMatchingChapter).Should().BeFalse(
                "mangaIds filter should reject rows whose MangaId is not in the supplied list");
        }

        [Test]
        public void GetCutoffUnmetChapters_skips_mangaIds_filter_when_mangaIds_empty_or_null()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            Subject.GetCutoffUnmetChapters(new PagingRequestResource(), monitored: false, mangaIds: Array.Empty<int>());

            capturedSpec.Should().NotBeNull();
            capturedSpec!.FilterExpressions.Should().BeEmpty(
                "empty mangaIds array should not add a FilterExpression");
        }

        [Test]
        public void GetCutoffUnmetChapters_hydrates_Manga_subresource_when_includeManga_true()
        {
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(42))
                .Returns(new NzbDrone.Core.Manga.Manga { Id = 42, Title = "Test Manga" });

            var result = Subject.GetCutoffUnmetChapters(new PagingRequestResource(), includeManga: true);

            // IMangaService.GetManga must be called exactly once for the single chapter row.
            Mocker.GetMock<IMangaService>()
                .Verify(s => s.GetManga(42), Times.Once);

            result.Should().BeOfType<Ok<PagingResource<MangaCutoffResource>>>();
            var ok = (Ok<PagingResource<MangaCutoffResource>>)result;

            // The Manga subresource must be populated on each row with {Id, Title}.
            var record = ok.Value!.Records.Single();
            record.Manga.Should().NotBeNull();
            record.Manga!.Id.Should().Be(42);
            record.Manga.Title.Should().Be("Test Manga");
        }

        [Test]
        public void GetCutoffUnmetChapters_skips_Manga_subresource_hydration_when_includeManga_false()
        {
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            // Default includeManga = false (no explicit arg).
            var result = Subject.GetCutoffUnmetChapters(new PagingRequestResource());

            Mocker.GetMock<IMangaService>()
                .Verify(
                    s => s.GetManga(It.IsAny<int>()),
                    Times.Never,
                    "includeManga=false must NOT trigger IMangaService.GetManga hydration");

            result.Should().BeOfType<Ok<PagingResource<MangaCutoffResource>>>();
            var ok = (Ok<PagingResource<MangaCutoffResource>>)result;
            ok.Value!.Records.Single().Manga.Should().BeNull(
                "Manga subresource must remain null when includeManga=false");
        }

        // ===================== F-CUTOFF-SIGNALR follow-up (2026-05-06) =====================
        // Phase-12 follow-up tests covering the SignalR refactor that landed in commit
        // b85529c58 (MangaCutoffController extends RestControllerWithSignalR<MangaCutoffResource,
        // Chapter> + subscribes to ChapterGrabbedEvent / ChapterImportedEvent /
        // ChapterFileDeletedEvent). Without these tests, a future Phase 8/15 collapse could
        // silently revert the controller to plain Controller and the page-auto-refresh
        // contract would silently break (no React Query invalidation on chapter/file pipeline
        // events).
        //
        // Predicate locked per MangaControllerSignalRFixture pattern (W6 revision iteration 1
        // shape extracted verbatim from RestControllerWithSignalR.cs:81-89): SignalRMessage
        // construction confirms Name = Resource = "manga/wanted/cutoff" + Action =
        // ModelAction.Updated. The "manga/wanted/cutoff" literal auto-derives from
        // [V5ApiController("manga/wanted/cutoff")] via RestControllerWithSignalR.cs:23-33.

        [Test]
        public void Controller_extends_RestControllerWithSignalR_per_F_CUTOFF_SIGNALR_followup()
        {
            // Pin the base-class shape so a future Phase 8/15 collapse cannot silently revert
            // to plain Controller (which would dead-letter the SignalR emission contract). The
            // assertion is on the open generic to avoid coupling to TResource/TModel name
            // changes — the LOAD-bearing fact is "this controller has the SignalR base".
            typeof(MangaCutoffController).BaseType.Should().NotBeNull();
            typeof(MangaCutoffController).BaseType!.IsGenericType.Should().BeTrue(
                "MangaCutoffController must extend a generic SignalR base — F-CUTOFF-SIGNALR contract");
            typeof(MangaCutoffController).BaseType!.GetGenericTypeDefinition()
                .Should().Be(typeof(RestControllerWithSignalR<,>),
                    "MangaCutoffController must extend RestControllerWithSignalR<,> so the React " +
                    "Query cache for ['/manga/wanted/cutoff'] auto-refreshes on chapter/file pipeline events");
        }

        [Test]
        public void Handle_ChapterGrabbedEvent_should_BroadcastResourceChange_Updated_for_each_chapter_in_RemoteChapter()
        {
            // RemoteChapter.Chapters is the resolved Chapter list per
            // src/NzbDrone.Core/Parser/Manga/Model/RemoteChapter.cs:32. The handler iterates
            // per-chapter and broadcasts Updated by id, mirroring TV
            // EpisodeControllerWithSignalR.Handle(EpisodeGrabbedEvent) at lines 122-132.
            var remoteChapter = new RemoteChapter
            {
                Chapters = new List<NzbDrone.Core.Manga.Chapter>
                {
                    new() { Id = 1, MangaId = 42, ChapterNumber = 1m },
                    new() { Id = 2, MangaId = 42, ChapterNumber = 2m },
                }
            };

            // ChapterGrabbedEvent ctor: (RemoteChapter, string downloadId, string downloadClient) —
            // verified src/NzbDrone.Core/MediaFiles/ChapterArchiving/ChapterGrabbedEvent.cs:17.
            Subject.Handle(new ChapterGrabbedEvent(remoteChapter, "dl-1", "InProcess"));

            // One broadcast per chapter id (Times.Once each). Predicate binds on Resource.Id
            // so a regression that captures only the first id (or always Id=1) fails the assertion.
            foreach (var id in new[] { 1, 2 })
            {
                var expectedId = id;
                Mocker.GetMock<IBroadcastSignalRMessage>()
                      .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                          m.Action == ModelAction.Updated &&
                          m.Name == "manga/wanted/cutoff" &&
                          ((ResourceChangeMessage<MangaCutoffResource>)m.Body).Resource.Id == expectedId)),
                          Times.Once);
            }
        }

        [Test]
        public void Handle_ChapterImportedEvent_should_BroadcastResourceChange_Updated_for_imported_chapter_id()
        {
            // ChapterImportedEvent carries a single Chapter (NOT a list) — manga import pipeline
            // imports one chapter per event per Plan 06-07 Pitfall 4 (publish AFTER ChapterFile
            // DB commit + filesystem move complete). Verify
            // src/NzbDrone.Core/MediaFiles/MangaImport/ChapterImportedEvent.cs:14-20.
            var importedEvent = new ChapterImportedEvent
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = 42, Title = "Test Manga" },
                Chapter = new NzbDrone.Core.Manga.Chapter { Id = 7, MangaId = 42, ChapterNumber = 3m },
                ChapterFile = new ChapterFile { Id = 99, MangaId = 42, ChapterId = 7 },
                NewDownload = true
            };

            Subject.Handle(importedEvent);

            // Single broadcast for the imported chapter's Id.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Updated &&
                      m.Name == "manga/wanted/cutoff" &&
                      ((ResourceChangeMessage<MangaCutoffResource>)m.Body).Resource.Id == 7)),
                      Times.Once);
        }

        [Test]
        public void Handle_ChapterFileDeletedEvent_should_BroadcastResourceChange_Updated_with_chapter_id()
        {
            // ChapterFileDeletedEvent ctor: (ChapterFile, DeleteMediaFileReason) — verified
            // src/NzbDrone.Core/MediaFiles/Events/ChapterFileDeletedEvent.cs:12. The ChapterFile
            // carries a single ChapterId (verified src/NzbDrone.Core/MediaFiles/ChapterFile.cs:14).
            // DeleteMediaFileReason.Manual bypasses the Upgrade short-circuit so the broadcast fires.
            var chapterFile = Builder<ChapterFile>.CreateNew()
                .With(c => c.Id = 99)
                .With(c => c.MangaId = 42)
                .With(c => c.ChapterId = 7)
                .Build();

            Subject.Handle(new ChapterFileDeletedEvent(chapterFile, DeleteMediaFileReason.Manual));

            // Single broadcast for the deleted file's ChapterId (NOT the file Id).
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Updated &&
                      m.Name == "manga/wanted/cutoff" &&
                      ((ResourceChangeMessage<MangaCutoffResource>)m.Body).Resource.Id == 7)),
                      Times.Once);
        }

        [Test]
        public void Handle_ChapterFileDeletedEvent_should_NOT_broadcast_when_reason_is_Upgrade()
        {
            // Upgrade-reason short-circuit mirrors MangaController.Handle(ChapterFileDeletedEvent)
            // at MangaController.cs:336-341: the upcoming Add event will fire next, so
            // broadcasting twice for one logical change is wasteful. Verify the short-circuit by
            // asserting BroadcastMessage was NEVER called.
            var chapterFile = Builder<ChapterFile>.CreateNew()
                .With(c => c.Id = 99)
                .With(c => c.MangaId = 42)
                .With(c => c.ChapterId = 7)
                .Build();

            Subject.Handle(new ChapterFileDeletedEvent(chapterFile, DeleteMediaFileReason.Upgrade));

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(
                      b => b.BroadcastMessage(It.IsAny<SignalRMessage>()),
                      Times.Never,
                      "Upgrade-reason ChapterFileDeletedEvent must NOT broadcast — the upcoming " +
                      "Add event fires next; mirrors MangaController.Handle(ChapterFileDeletedEvent) precedent");
        }
    }
}
