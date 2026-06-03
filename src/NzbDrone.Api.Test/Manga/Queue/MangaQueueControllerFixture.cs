#nullable enable
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
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller fixture introduced via debug session
    // .planning/debug/activity-badge-three-queue-empty.md (2026-05-11).
    //
    // Role-match analog: upstream src/Sonarr.Api.V5/Queue/QueueController.cs:142-310
    // (v5-develop) — the canonical Sonarr `[HttpGet] GetQueue([FromQuery] PagingRequestResource paging, ...)`
    // action returns `Ok<PagingResource<QueueResource>>` and applies in-memory sort + filter
    // + skip/take via the private `PagingSpec<Queue> GetQueue(PagingSpec<Queue>, ...)` helper.
    //
    // Trigger: live smoke-test surfaced an "Activity 3" sidebar badge while the `/manga/queue`
    // endpoint returned []. Root cause investigation revealed TWO layered defects:
    //   Layer 1 — MangaQueueController.GetQueue did NOT concat pending releases (in-flight
    //     only, no `queue.Concat(pending)`).
    //   Layer 2 — After Layer 1's concat fix, the API returned 3 items in a bare
    //     `List<MangaQueueResource>`, but the frontend hook uses `usePagedApiQuery<Queue>`
    //     which expects a `PagingResource<T>` envelope. React Query read `data?.records`
    //     → `undefined`, fell back to `DEFAULT_RECORDS = []`, and the Queue page still
    //     rendered empty. Canonical Sonarr `QueueController.GetQueue` returns
    //     `Ok<PagingResource<QueueResource>>` with full in-memory paging — restoring that
    //     shape on the manga peer is Layer 2 of the fix.
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test
    // does not project-reference Mangarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // src/NzbDrone.Api.Test/Manga/Queue/MangaQueueDetailsControllerFixture.cs (Plan 13-08 Rule 3
    // deviation — documented in src/Mangarr.Api.V5/Manga/CLAUDE.md).
    //
    // Note on default SortDirection: `PagingResource<T>` constructor at
    // src/Mangarr.Http/PagingResource.cs:35 sets SortDirection to Descending when the
    // request omits it (`requestResource.SortDirection ?? SortDirection.Descending`).
    // The controller's `defaultSortDirection: Ascending` in MapToPagingSpec is ONLY
    // applied when the request explicitly sends SortDirection.Default. Tests that
    // exercise the "default Ascending" path explicitly pass SortDirection.Default;
    // tests that omit sortDirection get the wire-level Descending default (matches
    // canonical Sonarr behavior verbatim — same shape on upstream QueueController.cs:144).
    //
    // Tests:
    //   1. Pattern 2 — Route literal pin (`manga/queue` — load-bearing for the frontend
    //      useQueue.ts:94 path string).
    //   2. Pattern 3 — Base-class assertion (RestControllerWithSignalR<,> generic base).
    //   3. Layer 2 envelope shape — GET returns `Ok<PagingResource<MangaQueueResource>>` with
    //      records / totalRecords / page / pageSize / sortKey populated.
    //   4. Layer 1 concat happy path — queue=2 + pending=3 → totalRecords=5, records.Count=5
    //      (no double-count).
    //   5. Pagination — 5 items, pageSize=2, page=2 → records.Count=2, totalRecords=5.
    //   6. Filter by mangaIds[] — narrows the union to matching rows.
    //   7. Default sort = `timeleft` Ascending — exercised via explicit SortDirection.Default
    //      (the canonical path that triggers MapToPagingSpec's defaultSortDirection arg).
    //   8. Live-bug regression — queue=[] + pending=3 → records.Count=3, totalRecords=3
    //      (the exact user scenario from the debug session).
    //   9. Pattern 5 — IHandle test for MangaQueueUpdatedEvent → Sync broadcast.
    //  10. Pattern 5 — IHandle test for MangaPendingReleasesUpdatedEvent → Sync broadcast
    //      (the new subscriber added alongside the concat fix — without it pending mutations
    //      would NOT re-fan SignalR to the React Activity Queue page).
    [TestFixture]
    public class MangaQueueControllerFixture : TestBase<MangaQueueController>
    {
        [SetUp]
        public void Setup()
        {
            // RestControllerWithSignalR short-circuits BroadcastMessage when IsConnected is
            // false. Force true so the Mocker observes BroadcastMessage in the IHandle tests.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(true);

            // Default empty queue + pending so GetQueue does not NRE.
            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(new List<MangaQueueItem>());

            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Setup(s => s.GetPendingQueue())
                  .Returns(new List<MangaQueueItem>());
        }

        private static PagingRequestResource Paging(int page = 1, int pageSize = 10, string? sortKey = null, SortDirection? sortDirection = null)
        {
            return new PagingRequestResource
            {
                Page = page,
                PageSize = pageSize,
                SortKey = sortKey!,
                SortDirection = sortDirection
            };
        }

        [Test]
        public void Route_attribute_is_manga_queue_literal()
        {
            // Plan 07-02 URL-shaped React Query key contract — the route attribute string is the
            // load-bearing contract between frontend useQueue.ts:94 (`/manga/queue`) and this
            // controller. Mismatched literal silently breaks every fetch from /manga/queue.
            // Pitfall 5 — TV/manga cache MUST NOT collide.
            var attr = (V5ApiControllerAttribute?)Attribute.GetCustomAttribute(
                typeof(MangaQueueController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr!.Resource.Should().Be("manga/queue");
        }

        [Test]
        public void Controller_extends_RestControllerWithSignalR_per_PATTERNS_md_pattern_3()
        {
            // Pin the base-class shape so a future Phase 8/15 collapse cannot silently revert to
            // plain Controller (which would dead-letter the SignalR emission contract). The
            // assertion is on the open generic to avoid coupling to TResource/TModel name
            // changes — the LOAD-bearing fact is "this controller has the SignalR base".
            typeof(MangaQueueController).BaseType.Should().NotBeNull();
            typeof(MangaQueueController).BaseType!.IsGenericType.Should().BeTrue(
                "MangaQueueController must extend a generic SignalR base");
            typeof(MangaQueueController).BaseType!.GetGenericTypeDefinition()
                .Should().Be(typeof(RestControllerWithSignalR<,>),
                    "MangaQueueController must extend RestControllerWithSignalR<MangaQueueResource, MangaQueueItem> " +
                    "so the React Query cache for ['/manga/queue'] auto-refreshes on queue mutation events");
        }

        [Test]
        public void GetQueue_returns_PagingResource_envelope_with_canonical_fields()
        {
            // Layer 2 envelope regression guard — frontend useQueue.ts uses usePagedApiQuery<Queue>
            // which reads `data?.records / .totalRecords / .page / .pageSize / .sortKey / .sortDirection`.
            // Before Layer 2 the GET returned a bare List<MangaQueueResource>; React Query read
            // `data?.records` → undefined and the page rendered empty. The canonical Sonarr
            // shape returns Ok<PagingResource<QueueResource>> per upstream QueueController.cs:142.
            var queue = new List<MangaQueueItem>
            {
                new() { Id = 1, MangaId = 42, Title = "In-flight A" },
            };
            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(queue);

            var result = Subject.GetQueue(Paging(), includeUnknownMangaItems: true);

            result.Should().BeOfType<Ok<PagingResource<MangaQueueResource>>>();
            var envelope = result.Value!;
            envelope.Records.Should().NotBeNull();
            envelope.Records.Should().HaveCount(1);
            envelope.TotalRecords.Should().Be(1);
            envelope.Page.Should().Be(1);
            envelope.PageSize.Should().Be(10);
            envelope.SortKey.Should().Be("timeleft",
                "controller-supplied default sort key wins when the request omits sortKey");
        }

        [Test]
        public void GetQueue_returns_union_of_in_flight_queue_and_pending_releases()
        {
            // Layer 1 canonical concat — mirrors upstream
            // src/Sonarr.Api.V5/Queue/QueueController.cs:183-188 (v5-develop):
            //   `var queue = _queueService.GetQueue(); ... var pending = _pendingReleaseService.GetPendingQueue();
            //    var fullQueue = filteredQueue.Concat(pending).Where(...)`.
            // Both shapes (in-flight + pending) populate the SAME MangaQueueItem POCO per
            // IMangaPendingReleaseService.GetPendingQueue() : List<MangaQueueItem>.
            //
            // Regression guard for activity-badge-three-queue-empty: pre-fix the GET returned only
            // _queueService.GetMangaQueue() (in-flight only), causing the badge to count
            // queue ∪ pending while the Queue page rendered queue only.
            var queue = new List<MangaQueueItem>
            {
                new() { Id = 1, MangaId = 42, Title = "In-flight A" },
                new() { Id = 2, MangaId = 7, Title = "In-flight B" },
            };
            var pending = new List<MangaQueueItem>
            {
                new() { Id = 3, MangaId = 99, Title = "Pending A" },
                new() { Id = 4, MangaId = 12, Title = "Pending B" },
                new() { Id = 5, MangaId = 13, Title = "Pending C" },
            };

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(queue);
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Setup(s => s.GetPendingQueue())
                  .Returns(pending);

            var result = Subject.GetQueue(Paging(pageSize: 10), includeUnknownMangaItems: true);

            var envelope = result.Value!;
            envelope.TotalRecords.Should().Be(5,
                "TotalRecords must reflect the union of queue (2) + pending (3) — canonical Sonarr " +
                "controller-layer concat per upstream QueueController.cs:183-188 (v5-develop)");
            envelope.Records.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 },
                "GetQueue must return the union of IMangaQueueService.GetMangaQueue (in-flight) " +
                "AND IMangaPendingReleaseService.GetPendingQueue (pending) — canonical Sonarr " +
                "controller-layer concat per upstream QueueController.cs:183-188 (v5-develop)");
            envelope.Records.Should().HaveCount(5, "no double-counting — each row appears exactly once");

            Mocker.GetMock<IMangaQueueService>()
                  .Verify(s => s.GetMangaQueue(), Times.Once);
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Verify(s => s.GetPendingQueue(), Times.Once);
        }

        [Test]
        public void GetQueue_paginates_via_PageSize_and_Page()
        {
            // Layer 2 pagination — page 2 of a pageSize=2 split over 5 items should return
            // 2 records (skip 2, take 2). TotalRecords stays at 5 across all pages.
            // Mirrors canonical Sonarr skip/take at upstream QueueController.cs:272.
            //
            // Exercise the Ascending-default path via SortDirection.Default so that
            // MapToPagingSpec's `defaultSortDirection: Ascending` arg is applied (matches
            // canonical Sonarr behavior). Without Default the wire-level Descending default
            // would reverse the row order; the pagination invariant (TotalRecords=5,
            // records.Count=2 on page=2) holds either way.
            var queue = new List<MangaQueueItem>
            {
                new() { Id = 1, MangaId = 1, Title = "Row 1", TimeLeft = TimeSpan.FromMinutes(1) },
                new() { Id = 2, MangaId = 2, Title = "Row 2", TimeLeft = TimeSpan.FromMinutes(2) },
                new() { Id = 3, MangaId = 3, Title = "Row 3", TimeLeft = TimeSpan.FromMinutes(3) },
                new() { Id = 4, MangaId = 4, Title = "Row 4", TimeLeft = TimeSpan.FromMinutes(4) },
                new() { Id = 5, MangaId = 5, Title = "Row 5", TimeLeft = TimeSpan.FromMinutes(5) },
            };
            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(queue);

            var result = Subject.GetQueue(
                Paging(page: 2, pageSize: 2, sortDirection: SortDirection.Default),
                includeUnknownMangaItems: true);

            var envelope = result.Value!;
            envelope.TotalRecords.Should().Be(5, "TotalRecords reflects total available, not the page size");
            envelope.Page.Should().Be(2);
            envelope.PageSize.Should().Be(2);
            envelope.Records.Should().HaveCount(2, "page 2 of pageSize=2 over 5 items yields 2 records");
            envelope.Records.Select(r => r.Id).Should().BeEquivalentTo(new[] { 3, 4 },
                "default sort is timeleft Ascending — page 2 skips Row 1 and Row 2, takes Row 3 and Row 4");
        }

        [Test]
        public void GetQueue_filters_by_mangaIds()
        {
            // Layer 1 manga-shape filter — replaces TV's `seriesIds[]`. Drops items where
            // MangaId is null or not in the filter set. Mirrors upstream QueueController.cs:194-197.
            var queue = new List<MangaQueueItem>
            {
                new() { Id = 1, MangaId = 100, Title = "Match" },
                new() { Id = 2, MangaId = 200, Title = "Skip" },
                new() { Id = 3, MangaId = 100, Title = "Match again" },
                new() { Id = 4, MangaId = null, Title = "Unknown manga" },
            };
            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(queue);

            var result = Subject.GetQueue(Paging(), includeUnknownMangaItems: true, mangaIds: new[] { 100 });

            var envelope = result.Value!;
            envelope.TotalRecords.Should().Be(2);
            envelope.Records.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 3 },
                "mangaIds filter narrows to MangaId == 100; null-MangaId rows are dropped");
        }

        [Test]
        public void GetQueue_default_sort_is_timeleft_ascending()
        {
            // Layer 2 default-sort contract — when no sortKey is supplied via the query string
            // AND the client explicitly sends SortDirection.Default, MapToPagingSpec falls
            // through to the controller-supplied defaults ("timeleft" + Ascending).
            // TimeLeft=null sinks to the bottom per TimeleftComparer at
            // src/NzbDrone.Core/Queue/TimeleftComparer.cs:15-23.
            var queue = new List<MangaQueueItem>
            {
                new() { Id = 1, MangaId = 1, Title = "Longest", TimeLeft = TimeSpan.FromHours(2) },
                new() { Id = 2, MangaId = 2, Title = "Null", TimeLeft = null },
                new() { Id = 3, MangaId = 3, Title = "Shortest", TimeLeft = TimeSpan.FromMinutes(5) },
                new() { Id = 4, MangaId = 4, Title = "Middle", TimeLeft = TimeSpan.FromMinutes(30) },
            };
            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(queue);

            var result = Subject.GetQueue(
                Paging(sortDirection: SortDirection.Default),
                includeUnknownMangaItems: true);

            var envelope = result.Value!;
            envelope.SortKey.Should().Be("timeleft");
            envelope.SortDirection.Should().Be(SortDirection.Ascending,
                "SortDirection.Default triggers MapToPagingSpec's defaultSortDirection arg " +
                "(SortDirection.Ascending) per Mangarr.Http/PagingResource.cs:61-63");
            envelope.Records.Select(r => r.Id).Should().BeEquivalentTo(new[] { 3, 4, 1, 2 },
                opts => opts.WithStrictOrdering(),
                "Ascending timeleft: Shortest (5m), Middle (30m), Longest (2h), Null (sinks to bottom)");
        }

        [Test]
        public void GetQueue_returns_pending_only_when_in_flight_queue_is_empty()
        {
            // Live-bug scenario from activity-badge-three-queue-empty: user has 3 pending releases
            // (e.g., delay-profile-held releases) and zero in-flight downloads. Pre-fix this GET
            // returned [] (queue-only projection), so the Queue page showed "Queue is empty" while
            // the sidebar badge said "Activity 3" (TotalCount = 0 + 3 = 3 from
            // MangaQueueStatusController). Post-fix the GET returns all 3 pending rows in the
            // canonical PagingResource envelope shape.
            var pending = new List<MangaQueueItem>
            {
                new() { Id = 101, MangaId = 1, Title = "Pending chapter 1" },
                new() { Id = 102, MangaId = 2, Title = "Pending chapter 2" },
                new() { Id = 103, MangaId = 3, Title = "Pending chapter 3" },
            };

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.GetMangaQueue())
                  .Returns(new List<MangaQueueItem>());
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Setup(s => s.GetPendingQueue())
                  .Returns(pending);

            var result = Subject.GetQueue(Paging(), includeUnknownMangaItems: true);

            var envelope = result.Value!;
            envelope.TotalRecords.Should().Be(3,
                "TotalRecords == 3 closes the badge-vs-page mismatch reported in " +
                ".planning/debug/activity-badge-three-queue-empty.md");
            envelope.Records.Should().HaveCount(3,
                "pending releases must surface through /manga/queue even when the in-flight " +
                "tracked-download queue is empty");
            envelope.Records.Select(r => r.Id).Should().BeEquivalentTo(new[] { 101, 102, 103 });
        }

        [Test]
        public void Handle_MangaQueueUpdatedEvent_should_BroadcastResourceChange_Sync()
        {
            // Pattern 5 IHandle test — mirrors TV QueueController.Handle(QueueUpdatedEvent)
            // verbatim. ModelAction.Sync uses the no-id overload of BroadcastResourceChange
            // (RestControllerWithSignalR.cs:93-114): it builds a SignalRMessage with no Resource
            // body, only Name + Action. The Name auto-derives from [V5ApiController("manga/queue")]
            // via RestControllerWithSignalR.cs:23-33.
            Subject.Handle(new MangaQueueUpdatedEvent());

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Sync && m.Name == "manga/queue")),
                      Times.Once);
        }

        [Test]
        public void Handle_MangaPendingReleasesUpdatedEvent_should_BroadcastResourceChange_Sync()
        {
            // NEW subscriber added alongside the GetQueue concat fix. Pre-fix this controller
            // did not subscribe to MangaPendingReleasesUpdatedEvent, so a pending release that
            // got promoted / held / removed would NOT re-fan SignalR to the React Activity Queue
            // page — the page would silently keep stale rows until manual refresh.
            //
            // Mirrors canonical Sonarr QueueController which subscribes to PendingReleasesUpdatedEvent
            // (upstream src/Sonarr.Api.V5/Queue/QueueController.cs:25) and the sibling manga
            // controllers MangaQueueDetailsController.cs:186-189 + MangaQueueStatusController.cs:121-125
            // which both already subscribe.
            Subject.Handle(new MangaPendingReleasesUpdatedEvent());

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Sync && m.Name == "manga/queue")),
                      Times.Once);
        }

        [Test]
        public void RemoveQueueItem_dispatches_in_flight_id_to_MangaQueueService()
        {
            // queue-remove-pending-no-op dispatch — in-flight branch. Find returns a non-null
            // MangaQueueItem so the handler routes to _queueService.Remove and DOES NOT call
            // _pendingReleaseService.RemovePendingQueueItems.
            const int InFlightId = 42;

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.Find(InFlightId))
                  .Returns(new MangaQueueItem { Id = InFlightId, MangaId = 1, Title = "In-flight" });

            Subject.RemoveQueueItem(InFlightId);

            Mocker.GetMock<IMangaQueueService>()
                  .Verify(s => s.Remove(InFlightId), Times.Once);
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Verify(s => s.RemovePendingQueueItems(It.IsAny<int>()),
                          Times.Never,
                          "in-flight ids must NOT fall through to the pending-release path");
        }

        [Test]
        public void RemoveQueueItem_dispatches_pending_id_to_MangaPendingReleaseService()
        {
            // queue-remove-pending-no-op dispatch — pending branch. Find returns null because
            // the id is in the pending hash-space (HashConverter.GetHashInt31("manga-pending-{Id}")),
            // not the in-flight hash-space (HashConverter.GetHashInt31("trackedDownload-...")).
            // Pre-fix this case silently no-op'd through _queueService.Remove; post-fix the
            // handler routes to _pendingReleaseService.RemovePendingQueueItems.
            //
            // Regression guard for .planning/debug/queue-remove-pending-no-op.md — clicking
            // Remove on a pending row was a silent 204-with-no-state-change before this fix.
            const int PendingId = 1885070670;

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.Find(PendingId))
                  .Returns((MangaQueueItem)null!);

            Subject.RemoveQueueItem(PendingId);

            Mocker.GetMock<IMangaQueueService>()
                  .Verify(s => s.Remove(It.IsAny<int>()),
                          Times.Never,
                          "pending ids must NOT call _queueService.Remove (silent no-op pre-fix)");
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Verify(s => s.RemovePendingQueueItems(PendingId),
                          Times.Once,
                          "pending ids must route to IMangaPendingReleaseService.RemovePendingQueueItems");
        }

        [Test]
        public void RemoveQueueItem_with_remove_true_evicts_the_job_from_the_owning_client()
        {
            // GH #309 regression guard — clicking Remove with "Remove from Download Client" must
            // call IDownloadClient.RemoveItem(item, deleteData:true) on the owning client so the
            // gateway job is actually deleted (pre-fix the controller only dropped the in-memory
            // projection row and the gateway job survived → the row reappeared on the next ~90s poll).
            const int InFlightId = 77;
            const string DownloadId = "j_gateway_1";
            const int ClientId = 5;

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.Find(InFlightId))
                  .Returns(new MangaQueueItem { Id = InFlightId, DownloadId = DownloadId });

            var downloadItem = new DownloadClientItem { DownloadId = DownloadId };
            var trackedDownload = new TrackedDownload { DownloadClient = ClientId, DownloadItem = downloadItem };

            Mocker.GetMock<IMangaDownloadMonitoringService>()
                  .Setup(s => s.GetTrackedDownloads())
                  .Returns(new List<TrackedDownload> { trackedDownload });

            var downloadClient = new Mock<IDownloadClient>();
            downloadClient.SetupGet(c => c.Definition)
                          .Returns(new DownloadClientDefinition { Id = ClientId });

            Mocker.GetMock<IDownloadClientFactory>()
                  .Setup(f => f.GetAvailableProviders())
                  .Returns(new List<IDownloadClient> { downloadClient.Object });

            Subject.RemoveQueueItem(InFlightId, remove: true);

            downloadClient.Verify(c => c.RemoveItem(downloadItem, true), Times.Once);
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(s => s.Remove(InFlightId), Times.Once);
        }

        [Test]
        public void RemoveQueueItem_with_remove_false_does_not_touch_the_download_client()
        {
            // GH #309 — unchecking "Remove from Download Client" must leave the client job alone
            // (only the projection row drops). The client-side path is guarded behind remove||blocklist.
            const int InFlightId = 78;

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.Find(InFlightId))
                  .Returns(new MangaQueueItem { Id = InFlightId, DownloadId = "j_gateway_2" });

            Subject.RemoveQueueItem(InFlightId, remove: false, blocklist: false);

            Mocker.GetMock<IMangaDownloadMonitoringService>()
                  .Verify(s => s.GetTrackedDownloads(), Times.Never, "with remove=false and blocklist=false there is no client-side work to do");
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(s => s.Remove(InFlightId), Times.Once);
        }

        private TrackedDownload BuildTrackedDownloadWithRemoteChapter(string downloadId, int clientId, int chapterId)
        {
            return new TrackedDownload
            {
                DownloadClient = clientId,
                DownloadItem = new DownloadClientItem { DownloadId = downloadId, Title = "Some Release" },
                RemoteChapter = new RemoteChapter
                {
                    Manga = new NzbDrone.Core.Manga.Manga { Id = 1 },
                    Chapters = new List<NzbDrone.Core.Manga.Chapter> { new NzbDrone.Core.Manga.Chapter { Id = chapterId } },
                    Release = new ReleaseInfo { Title = "Some Release", Indexer = "Manga Gateway", Guid = "g1" }
                }
            };
        }

        [Test]
        public void RemoveQueueItem_blocklist_without_skipRedownload_blocklists_and_searches()
        {
            // GH #309 — Sonarr-parity "Blocklist and Search": blocklist the release (manual flag so
            // the failure-budget AutoRetryOrchestrator stays out of it) AND fire a ChapterSearchCommand
            // for the chapter(s).
            const int InFlightId = 81;
            const string DownloadId = "j_bl_search";
            const int ChapterId = 5;

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.Find(InFlightId))
                  .Returns(new MangaQueueItem { Id = InFlightId, DownloadId = DownloadId });

            Mocker.GetMock<IMangaDownloadMonitoringService>()
                  .Setup(s => s.GetTrackedDownloads())
                  .Returns(new List<TrackedDownload> { BuildTrackedDownloadWithRemoteChapter(DownloadId, 5, ChapterId) });

            Subject.RemoveQueueItem(InFlightId, remove: false, blocklist: true, skipRedownload: false);

            Mocker.GetMock<IMangaBlocklistService>()
                  .Verify(s => s.Block(It.IsAny<MangaBlocklist>(), true), Times.Once);
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(It.Is<ChapterSearchCommand>(c => c.ChapterIds.Contains(ChapterId)), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once);
        }

        [Test]
        public void RemoveQueueItem_blocklist_with_skipRedownload_blocklists_without_searching()
        {
            // GH #309 — Sonarr-parity "Blocklist Only": blocklist the release but do NOT re-search.
            const int InFlightId = 82;
            const string DownloadId = "j_bl_only";
            const int ChapterId = 6;

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.Find(InFlightId))
                  .Returns(new MangaQueueItem { Id = InFlightId, DownloadId = DownloadId });

            Mocker.GetMock<IMangaDownloadMonitoringService>()
                  .Setup(s => s.GetTrackedDownloads())
                  .Returns(new List<TrackedDownload> { BuildTrackedDownloadWithRemoteChapter(DownloadId, 5, ChapterId) });

            Subject.RemoveQueueItem(InFlightId, remove: false, blocklist: true, skipRedownload: true);

            Mocker.GetMock<IMangaBlocklistService>()
                  .Verify(s => s.Block(It.IsAny<MangaBlocklist>(), true), Times.Once);
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(It.IsAny<ChapterSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never);
        }

        [Test]
        public void RemoveMany_dispatches_each_id_by_source()
        {
            // queue-remove-pending-no-op bulk dispatch — the same per-id source decision
            // applies inside the bulk DELETE loop. Pre-fix every id was routed to
            // _queueService.Remove, so bulk-removing pending items was a silent no-op.
            const int InFlightId = 100;
            const int PendingIdA = 200;
            const int PendingIdB = 300;

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.Find(InFlightId))
                  .Returns(new MangaQueueItem { Id = InFlightId, MangaId = 1, Title = "In-flight" });
            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.Find(PendingIdA))
                  .Returns((MangaQueueItem)null!);
            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.Find(PendingIdB))
                  .Returns((MangaQueueItem)null!);

            Subject.RemoveMany(new QueueBulkResource { Ids = new List<int> { InFlightId, PendingIdA, PendingIdB } });

            Mocker.GetMock<IMangaQueueService>()
                  .Verify(s => s.Remove(InFlightId), Times.Once);
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(s => s.Remove(PendingIdA), Times.Never);
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(s => s.Remove(PendingIdB), Times.Never);
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Verify(s => s.RemovePendingQueueItems(PendingIdA), Times.Once);
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Verify(s => s.RemovePendingQueueItems(PendingIdB), Times.Once);
        }

        [Test]
        public void RemoveMany_distincts_duplicate_ids_before_dispatch()
        {
            // Distinct() guard preserved post-fix — duplicate ids in the request body should
            // fire one dispatch per unique id, not one per occurrence. Otherwise a stray
            // duplicate would re-publish MangaPendingReleasesUpdatedEvent / MangaQueueUpdatedEvent
            // and double-broadcast SignalR Sync messages (UI thrash).
            const int PendingId = 999;

            Mocker.GetMock<IMangaQueueService>()
                  .Setup(s => s.Find(PendingId))
                  .Returns((MangaQueueItem)null!);

            Subject.RemoveMany(new QueueBulkResource { Ids = new List<int> { PendingId, PendingId, PendingId } });

            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Verify(s => s.RemovePendingQueueItems(PendingId),
                          Times.Once,
                          "Distinct() must collapse duplicate ids before dispatch");
        }
    }
}
