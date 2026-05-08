using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Mangarr.Api.V5.Manga.Queue;
using Mangarr.Http;

namespace NzbDrone.Api.Test.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 13 Plan 13-09 (D-13-04
    // forward-prophylactic). Role-match analog: src/Mangarr.Api.V5/Queue/QueueStatusController.cs
    // (TV peer ships no fixture upstream — Sonarr Api.Test does not have a QueueStatusControllerFixture;
    // this is the manga-side Wave 0 fixture introduced for the per-plan filter discipline
    // mandated by 13-PATTERNS.md S4 + plan acceptance criteria).
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test
    // does not project-reference Mangarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs (Plan 10-05 Rule 3 deviation
    // — documented in src/Mangarr.Api.V5/Manga/CLAUDE.md).
    //
    // Per-plan unit-test filter (PATTERNS.md S4 + plan acceptance criteria):
    //   dotnet test --filter "FullyQualifiedName~MangaQueueStatusController"
    // must return at least 1 passing test. This fixture provides 4 tests covering:
    //   1. Pattern 2 — Route literal pin (load-bearing for Plan 07-02 URL-shaped React Query
    //      key contract; the frontend useQueueStatus.ts:15 currently calls /queue/status and
    //      will be re-pointed at /manga/queue/status in a follow-up plan; mismatched literal
    //      silently breaks every fetch).
    //   2. Pattern 3 — Base-class assertion: MangaQueueStatusController extends
    //      RestControllerWithSignalR<MangaQueueStatusResource, MangaQueueItem>. Pin protects
    //      against silent regression to plain Controller (which would dead-letter SignalR).
    //   3. Happy-path GET: GetQueueStatus pulls from IMangaQueueService.GetMangaQueue +
    //      IMangaPendingReleaseService.GetPendingQueue and returns counter shape with
    //      TotalCount = queue.Count + pending.Count. Verifies the manga discriminator
    //      (q.MangaId.HasValue && q.MangaId > 0) routes the rows to Count vs UnknownCount
    //      correctly.
    //   4. IHandle<MangaQueueUpdatedEvent> dispatch: invoking Handle delegates to the
    //      Debouncer.Execute path — verified indirectly by exercising the handler without
    //      throwing (Debouncer is sealed-shape and not mock-friendly; the assertion is that
    //      the handler does not blow up and does not synchronously broadcast — the 5s timer
    //      defers fan-out per the verbatim TV peer pattern at QueueStatusController.cs:69-72).
    //
    // BroadcastResourceChange path (RestControllerWithSignalR.cs:52-67) requires
    // IBroadcastSignalRMessage.IsConnected to be true to observe BroadcastMessage. The Handle
    // tests exercise the immediate handler entry only (the actual broadcast is on the 5s timer
    // and would require real-clock or mock-Debouncer to assert; the load-bearing contract is
    // that Handle delegates to the Debouncer, which is verified by the no-throw + no-immediate-
    // broadcast assertion).
    [TestFixture]
    public class MangaQueueStatusControllerFixture : TestBase<MangaQueueStatusController>
    {
        [SetUp]
        public void Setup()
        {
            // Default: IsConnected = false so per-test broadcasts short-circuit unless a
            // specific test opts in by re-setting up the mock. The happy-path GET test
            // does NOT exercise the broadcast path (Pause/Resume around the read prevents
            // synchronous fan-out). The Handle test relies on Debouncer's 5s timer NOT
            // having elapsed by the time the assertion runs, which is structurally true.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(false);

            // Default empty queue + pending so GetQueueStatus does not NRE.
            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(new List<MangaQueueItem>());

            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Setup(s => s.GetPendingQueue())
                  .Returns(new List<MangaQueueItem>());
        }

        [Test]
        public void Route_attribute_is_manga_queue_status_literal_per_plan_07_02_contract()
        {
            // Plan 07-02 URL-shaped React Query key contract — the route attribute string is the
            // load-bearing contract between the frontend useQueueStatus hook and this controller.
            // Mismatched route literal silently breaks every fetch from /manga/queue/status
            // (the F-CUTOFF-class symptom Plan 13-09 forward-prophylactic backfill prevents).
            // Pitfall 5 — TV/manga cache MUST NOT collide.
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(MangaQueueStatusController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr.Resource.Should().Be("manga/queue/status");
        }

        [Test]
        public void Controller_extends_RestControllerWithSignalR()
        {
            // Pin the base-class shape so a future Phase 8/15 collapse cannot silently revert
            // to plain Controller (which would dead-letter the SignalR emission contract). The
            // assertion is on the open generic so a TResource/TModel type-name change does not
            // require a fixture update.
            typeof(MangaQueueStatusController).BaseType.Should().NotBeNull();
            typeof(MangaQueueStatusController).BaseType!.IsGenericType.Should().BeTrue(
                "MangaQueueStatusController must extend a generic SignalR base — D-13-04 contract");
            typeof(MangaQueueStatusController).BaseType!.GetGenericTypeDefinition()
                .Should().Be(typeof(Mangarr.Http.REST.RestControllerWithSignalR<,>),
                    "MangaQueueStatusController must extend RestControllerWithSignalR<,> so the React " +
                    "Query cache for ['/manga/queue/status'] auto-refreshes on queue mutation events");
        }

        [Test]
        public void GetQueueStatus_returns_counters_from_queue_and_pending_services()
        {
            // 3 queue items: 2 manga rows (MangaId>0), 1 unknown (MangaId=null);
            // 1 pending row. Expected TotalCount = 3 + 1 = 4; Count = 2 + 1 = 3 (Count
            // mirrors TV peer at QueueStatusController.cs:50 which adds pending.Count
            // to the manga-row count); UnknownCount = 1.
            var queue = new List<MangaQueueItem>
            {
                new() { MangaId = 42, TrackedDownloadStatus = "Ok" },
                new() { MangaId = 7, TrackedDownloadStatus = "Error" },
                new() { MangaId = null, TrackedDownloadStatus = "Warning" },
            };
            var pending = new List<MangaQueueItem>
            {
                new() { MangaId = 99, TrackedDownloadStatus = "Ok" },
            };

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(queue);
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Setup(s => s.GetPendingQueue())
                  .Returns(pending);

            var resource = Subject.GetQueueStatus();

            resource.Should().NotBeNull();
            resource.TotalCount.Should().Be(4, "TotalCount = queue.Count + pending.Count per QueueStatusController.cs:49 verbatim");
            resource.Count.Should().Be(3, "Count = manga-row count (2) + pending.Count (1) — TV peer adds pending into Count, not UnknownCount");
            resource.UnknownCount.Should().Be(1, "Single null-MangaId row routes to UnknownCount");
            resource.Errors.Should().BeTrue("Manga row with TrackedDownloadStatus='Error' triggers Errors flag");
            resource.Warnings.Should().BeFalse("No manga row carries TrackedDownloadStatus='Warning' (the Warning row has MangaId=null so it routes to UnknownWarnings)");
            resource.UnknownWarnings.Should().BeTrue("Null-MangaId row with TrackedDownloadStatus='Warning' triggers UnknownWarnings");
            resource.UnknownErrors.Should().BeFalse("No null-MangaId row carries TrackedDownloadStatus='Error'");

            Mocker.GetMock<IMangaQueueService>().Verify(s => s.GetMangaQueue(), Times.Once);
            Mocker.GetMock<IMangaPendingReleaseService>().Verify(s => s.GetPendingQueue(), Times.Once);
        }

        [Test]
        public void Handle_MangaQueueUpdatedEvent_does_not_throw_and_defers_broadcast_via_debouncer()
        {
            // Debouncer is concrete (NzbDrone.Common.TPL.Debouncer) and not mock-friendly — its
            // 5s timer is private. The load-bearing contract is that Handle delegates to
            // _broadcastDebounce.Execute() which sets the timer; the actual fan-out happens
            // 5s later on a background thread (out of scope for this fixture). Verify the
            // handler entry by exercising the call path and asserting:
            //   (a) no exception is thrown (catches a regression that wires the handler to
            //       _broadcastDebounce.Pause() or another sync-throwing path);
            //   (b) BroadcastMessage is NOT called synchronously (proves the debounce defers
            //       the fan-out — IsConnected is true here so a non-debounced impl WOULD
            //       broadcast).
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(true);

            Action invoke = () => Subject.Handle(new MangaQueueUpdatedEvent());
            invoke.Should().NotThrow("Handle(MangaQueueUpdatedEvent) must delegate to the Debouncer without throwing");

            // No synchronous broadcast — the 5s debounce defers fan-out. Even though
            // IsConnected is true, BroadcastMessage must not have been called by the time
            // this assertion runs.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(
                      b => b.BroadcastMessage(It.IsAny<SignalRMessage>()),
                      Times.Never,
                      "Handle must NOT broadcast synchronously — the 5s Debouncer defers fan-out");
        }
    }
}
