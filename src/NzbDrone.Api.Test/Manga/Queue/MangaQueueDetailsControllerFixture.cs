using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Mangarr.Api.V5.Manga.Queue;
using Mangarr.Http;
using Mangarr.Http.REST;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
namespace NzbDrone.Api.Test.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 13 Plan 13-08
    // (sub-wave-B-addition — D-13-04 forward-prophylactic backfill of MangaQueueDetailsController).
    //
    // Role-match analog: there is no TV `QueueDetailsControllerFixture` in v1 (TV-side is covered
    // only at the integration layer); the closest filing peer is
    // src/NzbDrone.Api.Test/Manga/Wanted/MangaCutoffControllerFixture.cs (Phase 12 Plan 12-12 +
    // F-CUTOFF-SIGNALR follow-up — IBroadcastSignalRMessage + IsConnected SetUp + per-handler
    // Verify(...) predicate-locking pattern). MangaCutoffControllerFixture is the canonical manga
    // V5 controller fixture shape post-Phase-12 (TestBase<TController> + AutoMoqer + per-IHandle
    // round-trip BroadcastResourceChange test).
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test
    // does not project-reference Mangarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs and
    // src/NzbDrone.Api.Test/Manga/Wanted/MangaCutoffControllerFixture.cs (Plan 10-05 / Plan 12-12
    // Rule 3 deviation — documented in src/Mangarr.Api.V5/Manga/CLAUDE.md lines 108-111).
    //
    // Per-plan unit-test filter (PATTERNS.md S4): `dotnet test --filter
    // "FullyQualifiedName~MangaQueueDetailsController"` must return >= 1 passing test. This
    // fixture provides 4 tests covering the load-bearing wave-0 patterns:
    //   1. Pattern 2 — Route literal pin: reflective V5ApiControllerAttribute lookup confirms
    //      `attr.Resource == "manga/queue/details"` (Plan 07-02 URL-shaped React Query key
    //      contract — the route literal is the load-bearing contract between any future React
    //      hook and this controller; Pitfall 5 — TV/manga cache MUST NOT collide).
    //   2. Pattern 3 — Base-class assertion: MangaQueueDetailsController extends
    //      `RestControllerWithSignalR<MangaQueueResource, MangaQueueItem>` (NOT plain Controller).
    //      Pin protects against silent regression that would dead-letter the SignalR fan-out
    //      contract for resource `manga/queue/details`.
    //   3. Happy-path delegation: GetQueue with a `mangaId` filter delegates to
    //      IMangaQueueService.GetMangaQueue + IMangaPendingReleaseService.GetPendingQueue and
    //      returns only rows whose MangaQueueItem.MangaId matches.
    //   4. Pattern 5 — IHandle test: Handle(MangaQueueUpdatedEvent) calls
    //      BroadcastResourceChange(ModelAction.Sync) — observable as
    //      IBroadcastSignalRMessage.BroadcastMessage with `m.Action == ModelAction.Sync` AND
    //      `m.Name == "manga/queue/details"` (resource literal auto-derives from the
    //      [V5ApiController("manga/queue/details")] attribute via RestControllerWithSignalR.cs:23-33).
    //
    // BroadcastResourceChange path (RestControllerWithSignalR.cs:93-114) for ModelAction.Sync
    // does NOT round-trip through GetResourceById (the no-id overload at line 93 builds the
    // SignalRMessage directly). So we only need to wire IBroadcastSignalRMessage.IsConnected = true
    // — no IMangaQueueService.Find SetUp is needed for the IHandle test.
    [TestFixture]
    public class MangaQueueDetailsControllerFixture : TestBase<MangaQueueDetailsController>
    {
        [SetUp]
        public void Setup()
        {
            // RestControllerWithSignalR short-circuits BroadcastMessage when IsConnected
            // is false. Force true so the Mocker observes the BroadcastMessage call in
            // the IHandle<MangaQueueUpdatedEvent> test below.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(true);
        }

        [Test]
        public void Route_attribute_is_manga_queue_details_literal()
        {
            // Plan 07-02 URL-shaped React Query key contract — the route attribute string is
            // the load-bearing contract between any future React `useQueueDetails` hook (manga
            // counterpart of QueueDetailsProvider.tsx:31) and this controller. Mismatched route
            // literal silently re-routes manga-domain fetches to the TV `/queue/details`
            // controller (the F-CUTOFF symptom Plan 12-99 Task 7 surfaced before Plan 12-12
            // shipped — Pitfall 5: TV/manga cache MUST NOT collide).
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(MangaQueueDetailsController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr.Resource.Should().Be("manga/queue/details");
        }

        [Test]
        public void Controller_extends_RestControllerWithSignalR_per_PATTERNS_md_pattern_3()
        {
            // Pin the base-class shape so a future Phase 8/15 collapse cannot silently revert
            // to plain Controller (which would dead-letter the SignalR emission contract). The
            // assertion is on the open generic to avoid coupling to TResource/TModel name
            // changes — the LOAD-bearing fact is "this controller has the SignalR base".
            typeof(MangaQueueDetailsController).BaseType.Should().NotBeNull();
            typeof(MangaQueueDetailsController).BaseType!.IsGenericType.Should().BeTrue(
                "MangaQueueDetailsController must extend a generic SignalR base — D-13-04 contract");
            typeof(MangaQueueDetailsController).BaseType!.GetGenericTypeDefinition()
                .Should().Be(typeof(RestControllerWithSignalR<,>),
                    "MangaQueueDetailsController must extend RestControllerWithSignalR<MangaQueueResource, MangaQueueItem> " +
                    "so SignalR fan-out for resource 'manga/queue/details' actually wires through");
        }

        [Test]
        public void GetQueue_with_mangaId_filters_by_manga()
        {
            // Mirrors TV QueueDetailsController.GetQueue happy-path: queue + pending are
            // concatenated, then filtered by the supplied mangaId. The MangaQueueItem rows whose
            // MangaId does NOT match must be excluded; rows whose MangaId DOES match must be
            // included exactly once.
            var queue = new List<MangaQueueItem>
            {
                new() { Id = 1, MangaId = 42 },
                new() { Id = 2, MangaId = 99 }, // excluded — different manga
            };
            var pending = new List<MangaQueueItem>
            {
                new() { Id = 3, MangaId = 42 },
                new() { Id = 4, MangaId = 7 }, // excluded — different manga
            };

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(queue);
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Setup(s => s.GetPendingQueue())
                  .Returns(pending);

            var result = Subject.GetQueue(mangaId: 42, chapterIds: new List<int>());

            result.Should().BeOfType<Ok<List<MangaQueueResource>>>();
            var ok = (Ok<List<MangaQueueResource>>)result;
            ok.Value!.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 3 },
                "GetQueue(mangaId: 42) must return only rows whose MangaQueueItem.MangaId == 42, " +
                "drawn from BOTH IMangaQueueService.GetMangaQueue and IMangaPendingReleaseService.GetPendingQueue");
        }

        [Test]
        public void Handle_MangaQueueUpdatedEvent_should_BroadcastResourceChange_Sync()
        {
            // Pattern 5 IHandle test — mirrors TV QueueDetailsController.Handle(QueueUpdatedEvent)
            // at QueueDetailsController.cs:65-69 (single-line BroadcastResourceChange(ModelAction.Sync)).
            //
            // ModelAction.Sync uses the no-id overload of BroadcastResourceChange
            // (RestControllerWithSignalR.cs:93-114): it builds a SignalRMessage with no Resource
            // body, only Name + Action. The Name auto-derives from
            // [V5ApiController("manga/queue/details")] via RestControllerWithSignalR.cs:23-33.
            Subject.Handle(new MangaQueueUpdatedEvent());

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Sync && m.Name == "manga/queue/details")),
                      Times.Once);
        }

        // ===================== CR-04 (Phase 13 Plan 13-13) — chapterIds null-guard =====================

        [Test]
        public void GetQueue_with_null_chapterIds_returns_NoContent_or_full_list_without_NRE()
        {
            // CR-04 null-guard: ASP.NET Core MVC binds query parameter arrays to NULL
            // (NOT an empty list) when the parameter is missing entirely from the query
            // string AND the binding configuration prefers null over empty. The previous
            // signature `[FromQuery] List<int> chapterIds` was non-nullable with no default
            // — passing null at the model-binder layer landed in the chapterIds.Any() branch
            // with chapterIds == null → NRE.
            //
            // Fix verified at this fixture: invoking GetQueue(mangaId: null, chapterIds: null,
            // includeSubresources: null) MUST NOT NRE. Behavior contract: with no filter
            // selectors set, fall through to the "else" branch that returns the full queue
            // (the same shape as the no-params HTTP request `GET /api/v5/manga/queue/details`).
            var queue = new List<MangaQueueItem>
            {
                new() { Id = 1, MangaId = 42 },
                new() { Id = 2, MangaId = 99 },
            };
            var pending = new List<MangaQueueItem>
            {
                new() { Id = 3, MangaId = 7 },
            };

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(queue);
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Setup(s => s.GetPendingQueue())
                  .Returns(pending);

            // Direct call mirrors the model-binder's null delivery on the missing-param path.
            // No throw expected — null chapterIds + null mangaId + null includeSubresources
            // must yield the full concatenated queue (3 rows) without NRE.
            Ok<List<MangaQueueResource>> result = null!;
            Assert.DoesNotThrow(() => result = Subject.GetQueue(mangaId: null, chapterIds: null, includeSubresources: null));

            result.Should().NotBeNull("GetQueue with all-null filter params must succeed without throwing");
            result.Value!.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 },
                "with no filter selector set the response is the full concatenated queue + pending list, " +
                "exactly as the no-filter `GET /api/v5/manga/queue/details` HTTP shape");
        }
    }
}
