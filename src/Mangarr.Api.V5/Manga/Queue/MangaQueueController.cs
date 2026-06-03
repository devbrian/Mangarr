// Sonarr divergence: Phase 15 Plan 15-10 — V5/Queue/ DELETED; QueueBulkResource relocated to V5/Manga/Queue/.
using Mangarr.Http;
using Mangarr.Http.Extensions;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Mangarr.Api.V5/Queue/QueueController.cs (lines 25-408).
    //
    // PIPELINE-03: paged queue projection + SignalR push on every queue mutation.
    //
    // Manga sibling preserves: RestControllerWithSignalR<TResource, TModel> + IHandle<...>
    // SignalR push shape; BroadcastResourceChange(ModelAction.Sync) on every queue mutation.
    //
    // Manga sibling diverges from QueueController:
    //   * Subscribe to MangaQueueUpdatedEvent (Plan 06-05) + MangaPendingReleasesUpdatedEvent
    //     (Phase 6 D-20 / Phase 9 D-09-06) instead of QueueUpdatedEvent +
    //     PendingReleasesUpdatedEvent.
    //   * Inject IMangaQueueService + IMangaPendingReleaseService (no failed-download /
    //     ignored-download / blocklist plumbing beyond the simpler manga lifecycle).
    //   * SignalR resource name = "mangaqueue" via [V5ApiController("manga/queue")] route +
    //     RestControllerWithSignalR's Resource property derived from the route attribute.
    //   * GET returns the union of in-flight (IMangaQueueService.GetMangaQueue()) +
    //     pending releases (IMangaPendingReleaseService.GetPendingQueue()) — mirrors the
    //     canonical Sonarr controller-layer concat at upstream
    //     src/Sonarr.Api.V5/Queue/QueueController.cs:142-310 (`filteredQueue.Concat(pending)` +
    //     paging envelope). Static-list projection from TrackedDownloadRefreshedEvent —
    //     typically <100 in-flight rows, in-memory sort/filter/skip-take is cheap.
    //   * Drops TV's `languages[]` + `quality[]` filters (manga has no quality model per
    //     Phase 4 D-04 + Phase 5 D-04; TranslatedLanguage is BCP-47 string, not a Language
    //     enum). Keeps `mangaIds[]` + `protocol` + `status[]` + `includeUnknownMangaItems`.
    //   * Drops TV-specific sort keys (`series.sortTitle`, `episode.airDateUtc`,
    //     `episode.title`, `quality`, `language`); adds manga-shape `manga.sortTitle` +
    //     `chapter.chapterNumber` (LIMITATION: manga shape carries SortTitle on the
    //     Manga aggregate but the projection POCO carries only Manga (eager-loaded). The
    //     `manga.sortTitle` sort key falls through to the Manga.SortTitle navigation
    //     property when Manga is hydrated; falls back to `q.Title` when Manga is null
    //     — same null-coalesce pattern as TV peer at QueueController.cs:295).
    //
    // Sonarr-canonical fix (activity-badge-three-queue-empty, 2026-05-11):
    //
    //   Layer 1 — Controller-layer concat (queue ∪ pending):
    //     Before the fix this GET only returned `_queueService.GetMangaQueue()` (in-flight only).
    //     `MangaQueueStatusController.GetQueueStatus` and `MangaQueueDetailsController.GetQueue`
    //     already unioned pending releases into TotalCount / the details projection respectively,
    //     so the sidebar badge counted pending releases (TotalCount=3) but the `/manga/queue`
    //     endpoint returned []. The Activity Queue page rendered "Queue is empty" while the
    //     sidebar badge claimed 3 items. Canonical Sonarr unions queue+pending at the
    //     controller layer — restoring that shape on the manga peer closes the badge-vs-page
    //     mismatch.
    //
    //   Layer 2 — PagingResource envelope (canonical paging shape):
    //     After the concat fix landed, the API returned 3 items in a bare `List<MangaQueueResource>`,
    //     but the frontend hook `frontend/src/Activity/Queue/useQueue.ts:96` uses
    //     `usePagedApiQuery<Queue>` which expects a `PagingResource<T>` envelope shape
    //     (`{ records: T[], totalRecords, page, pageSize, sortKey, sortDirection, totalPages }`
    //     per `frontend/src/Helpers/Hooks/usePagedApiQuery.ts:18-26 + line 79 + line 98`).
    //     React Query received the bare array, read `data?.records` → `undefined`, fell back to
    //     `DEFAULT_RECORDS = []`, and the page rendered empty. Canonical Sonarr's
    //     `QueueController.GetQueue` returns `Ok<PagingResource<QueueResource>>` with full
    //     in-memory paging (sort + filter + skip/take) — see upstream
    //     `src/Sonarr.Api.V5/Queue/QueueController.cs:142-310` (v5-develop). Phase 6 Plan 06-09
    //     simplified to a bare list (no paging), which was an undocumented divergence from
    //     canonical Sonarr that broke the frontend contract. Layer 2 of the fix restores the
    //     canonical paging envelope. See debug session
    //     .planning/debug/activity-badge-three-queue-empty.md.
    //
    // Sonarr-canonical fix (queue-remove-pending-no-op, 2026-05-13):
    //
    //   GH issue / debug session .planning/debug/queue-remove-pending-no-op.md.
    //   Symptom: clicking Remove on a pending row was a silent no-op (HTTP 204 with no state
    //   change). Root cause: the queue projection returned by GetQueue is the UNION of
    //   in-flight rows (IMangaQueueService) AND pending-release rows (IMangaPendingReleaseService);
    //   each source assigns deterministic ids in disjoint hash-spaces. The DELETE handlers
    //   only dispatched to _queueService.Remove(id), which silently returns when the id is
    //   not present in the in-flight static list. Pending-source ids never matched, so the
    //   row stayed in the projection (the next GET re-included it from the pending side).
    //   Fix: dispatch by source. _queueService.Find(id) decides which side owns the id; if
    //   in-flight, call _queueService.Remove(id); otherwise call
    //   _pendingReleaseService.RemovePendingQueueItems(id) (mirrors canonical Sonarr
    //   QueueController.Remove which routes to _pendingReleaseService.RemovePendingQueueItems
    //   on the pending branch — upstream src/Sonarr.Api.V5/Queue/QueueController.cs:64-86 +
    //   PendingReleaseService.RemovePendingQueueItems shape). Same dispatch applied per-id
    //   inside RemoveMany bulk loop.
    //
    // Phase 8 cleanup: collapse with QueueController when Tv/ deletes.
    [V5ApiController("manga/queue")]
    public class MangaQueueController : RestControllerWithSignalR<MangaQueueResource, MangaQueueItem>,
                                        IHandle<MangaQueueUpdatedEvent>, IHandle<MangaPendingReleasesUpdatedEvent>
    {
        private readonly IMangaQueueService _queueService;
        private readonly IMangaPendingReleaseService _pendingReleaseService;
        private readonly IMangaDownloadMonitoringService _monitoringService;
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly IMangaBlocklistService _blocklistService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public MangaQueueController(IBroadcastSignalRMessage broadcastSignalRMessage,
                                    IMangaQueueService queueService,
                                    IMangaPendingReleaseService pendingReleaseService,
                                    IMangaDownloadMonitoringService monitoringService,
                                    IDownloadClientFactory downloadClientFactory,
                                    IMangaBlocklistService blocklistService,
                                    IManageCommandQueue commandQueueManager,
                                    Logger logger)
            : base(broadcastSignalRMessage)
        {
            _queueService = queueService;
            _pendingReleaseService = pendingReleaseService;
            _monitoringService = monitoringService;
            _downloadClientFactory = downloadClientFactory;
            _blocklistService = blocklistService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        [NonAction]
        public override Results<Ok<MangaQueueResource>, NotFound> GetResourceByIdWithErrorHandler(int id)
        {
            return base.GetResourceByIdWithErrorHandler(id);
        }

        protected override MangaQueueResource? GetResourceById(int id)
        {
            var item = _queueService.Find(id);
            if (item == null)
            {
                throw new NotFoundException();
            }

            return item.ToResource();
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<PagingResource<MangaQueueResource>> GetQueue([FromQuery] PagingRequestResource paging,
                                                               bool includeUnknownMangaItems = true,
                                                               [FromQuery] int[]? mangaIds = null,
                                                               DownloadProtocol? protocol = null,
                                                               [FromQuery] QueueStatus[]? status = null)
        {
            // Canonical Sonarr paging shape — mirrors upstream
            // src/Sonarr.Api.V5/Queue/QueueController.cs:142-174 (v5-develop). Allowed sort keys
            // are manga-shape:
            //   * Drop TV-only keys: series.sortTitle, episode.airDateUtc, episode.title,
            //     episodes.airDateUtc, episodes.title, language, languages, quality.
            //   * Add manga-shape keys: manga.sortTitle, chapter.chapterNumber.
            //   * Keep shape-neutral keys: added, downloadClient, estimatedCompletionTime,
            //     indexer, progress, protocol, size, status, timeleft, title.
            // Default sort = "timeleft" Ascending (TV peer convention preserved).
            var pagingResource = new PagingResource<MangaQueueResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<MangaQueueResource, MangaQueueItem>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "added",
                    "chapter",
                    "chapter.chapterNumber",
                    "chapters",
                    "chapters.chapterNumber",
                    "downloadClient",
                    "estimatedCompletionTime",
                    "indexer",
                    "manga.sortTitle",
                    "progress",
                    "protocol",
                    "size",
                    "status",
                    "timeleft",
                    "title"
                },
                "timeleft",
                SortDirection.Ascending);

            return TypedResults.Ok(pagingSpec.ApplyToPage(
                spec => GetQueuePaged(spec, mangaIds?.ToHashSet() ?? new HashSet<int>(), protocol, status?.ToHashSet() ?? new HashSet<QueueStatus>(), includeUnknownMangaItems),
                q => q.ToResource()!));
        }

        // Mirrors canonical Sonarr private helper at upstream
        // src/Sonarr.Api.V5/Queue/QueueController.cs:176-275 (v5-develop). Drops TV-specific
        // filter dimensions (languages, quality) per Phase 4 D-04 + Phase 5 D-04. Compares the
        // QueueStatus enum filter values against the manga item's string Status field via
        // .ToString() (MangaQueueItem.Status is populated from `QueueStatus.ToString()` in
        // MangaQueueService.MapQueueItem:149 + MangaPendingReleaseService projection — same
        // pattern as MangaQueueStatusController:99-102 which compares TrackedDownloadStatus
        // string against enum-name string literals "Error" / "Warning").
        private PagingSpec<MangaQueueItem> GetQueuePaged(PagingSpec<MangaQueueItem> pagingSpec,
                                                        HashSet<int> mangaIds,
                                                        DownloadProtocol? protocol,
                                                        HashSet<QueueStatus> status,
                                                        bool includeUnknownMangaItems)
        {
            var ascending = pagingSpec.SortDirection == SortDirection.Ascending;
            var orderByFunc = GetOrderByFunc(pagingSpec);

            // Defensive copies already returned by both GetMangaQueue() and GetPendingQueue() at
            // the service layer.
            var queue = _queueService.GetMangaQueue();
            var filteredQueue = includeUnknownMangaItems ? queue : queue.Where(q => q.MangaId.HasValue && q.MangaId.Value > 0);
            var pending = _pendingReleaseService.GetPendingQueue();

            var hasMangaIdFilter = mangaIds is { Count: > 0 };
            var hasStatusFilter = status is { Count: > 0 };

            // Pre-compute status string set for case-sensitive ordinal match against the string
            // Status field on MangaQueueItem.
            var statusStrings = hasStatusFilter
                ? status.Select(s => s.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null;

            var fullQueue = filteredQueue.Concat(pending).Where(q =>
            {
                var include = true;

                if (hasMangaIdFilter)
                {
                    include &= q.MangaId.HasValue && mangaIds.Contains(q.MangaId.Value);
                }

                if (include && protocol.HasValue)
                {
                    include &= q.Protocol == protocol.Value;
                }

                if (include && hasStatusFilter)
                {
                    include &= q.Status != null && statusStrings!.Contains(q.Status);
                }

                return include;
            }).ToList();

            IOrderedEnumerable<MangaQueueItem> ordered;

            if (pagingSpec.SortKey == "timeleft")
            {
                ordered = ascending
                    ? fullQueue.OrderBy(q => q.TimeLeft, new TimeleftComparer())
                    : fullQueue.OrderByDescending(q => q.TimeLeft, new TimeleftComparer());
            }
            else if (pagingSpec.SortKey == "estimatedCompletionTime")
            {
                ordered = ascending
                    ? fullQueue.OrderBy(q => q.EstimatedCompletionTime, new DatetimeComparer())
                    : fullQueue.OrderByDescending(q => q.EstimatedCompletionTime, new DatetimeComparer());
            }
            else if (pagingSpec.SortKey == "added")
            {
                ordered = ascending
                    ? fullQueue.OrderBy(q => q.Added, new DatetimeComparer())
                    : fullQueue.OrderByDescending(q => q.Added, new DatetimeComparer());
            }
            else if (pagingSpec.SortKey == "protocol")
            {
                ordered = ascending
                    ? fullQueue.OrderBy(q => q.Protocol)
                    : fullQueue.OrderByDescending(q => q.Protocol);
            }
            else if (pagingSpec.SortKey == "indexer")
            {
                ordered = ascending
                    ? fullQueue.OrderBy(q => q.Indexer, StringComparer.InvariantCultureIgnoreCase)
                    : fullQueue.OrderByDescending(q => q.Indexer, StringComparer.InvariantCultureIgnoreCase);
            }
            else if (pagingSpec.SortKey == "downloadClient")
            {
                ordered = ascending
                    ? fullQueue.OrderBy(q => q.DownloadClient, StringComparer.InvariantCultureIgnoreCase)
                    : fullQueue.OrderByDescending(q => q.DownloadClient, StringComparer.InvariantCultureIgnoreCase);
            }
            else
            {
                ordered = ascending ? fullQueue.OrderBy(orderByFunc) : fullQueue.OrderByDescending(orderByFunc);
            }

            // Secondary sort by progress (descending) — same shape as TV peer at
            // QueueController.cs:271. Guards against divide-by-zero on Size==0 rows.
            ordered = ordered.ThenByDescending(q => q.Size == 0 ? 0 : 100 - ((double)q.SizeLeft / q.Size * 100));

            pagingSpec.TotalRecords = fullQueue.Count;
            pagingSpec.Records = ordered.Skip((pagingSpec.Page - 1) * pagingSpec.PageSize).Take(pagingSpec.PageSize).ToList();

            // Out-of-range page guard — mirrors TV peer at QueueController.cs:274-278. If a
            // client requests page > totalPages, snap back to the last page rather than
            // returning an empty result while TotalRecords > 0 (would confuse pagination UI).
            if (pagingSpec.Records.Empty() && pagingSpec.Page > 1)
            {
                pagingSpec.Page = (int)Math.Max(Math.Ceiling((decimal)(pagingSpec.TotalRecords / pagingSpec.PageSize)), 1);
                pagingSpec.Records = ordered.Skip((pagingSpec.Page - 1) * pagingSpec.PageSize).Take(pagingSpec.PageSize).ToList();
            }

            return pagingSpec;
        }

        // Mirrors canonical Sonarr private helper at upstream
        // src/Sonarr.Api.V5/Queue/QueueController.cs:277-310 (v5-develop). Drops TV-specific
        // sort cases (series.sortTitle, episode.*, languages, quality); adds manga.sortTitle
        // + chapter.chapterNumber. Default fall-through is TimeLeft to match the default sort
        // direction supplied to MapToPagingSpec.
        private static Func<MangaQueueItem, object?> GetOrderByFunc(PagingSpec<MangaQueueItem> pagingSpec)
        {
            switch (pagingSpec.SortKey)
            {
                case "status":
                    return q => q.Status ?? string.Empty;
                case "manga.sortTitle":
                    return q => q.Manga?.SortTitle ?? q.Title ?? string.Empty;
                case "title":
                    return q => q.Title ?? string.Empty;
                case "chapter":
                case "chapters":
                case "chapter.chapterNumber":
                case "chapters.chapterNumber":
                    // ChapterNumber is decimal (non-nullable on Chapter.cs:21). Sort null
                    // Chapters as decimal.MinValue so they sink to the bottom under Ascending
                    // and rise to the top under Descending — same shape as TV peer's
                    // `q.Episodes.FirstOrDefault()?.AirDateUtc ?? DateTime.MinValue` pattern
                    // at QueueController.cs:290.
                    return q => q.Chapter == null ? decimal.MinValue : q.Chapter.ChapterNumber;
                case "size":
                    return q => q.Size;
                case "progress":
                    // Avoid exploding if a download's size is 0 (mirrors TV peer at QueueController.cs:305).
                    return q => 100 - (q.SizeLeft / Math.Max(q.Size * 100, 1));
                default:
                    return q => q.TimeLeft;
            }
        }

        // Source-dispatch remove: the queue projection returned by GetQueue is the UNION of
        // in-flight rows (IMangaQueueService) and pending-release rows (IMangaPendingReleaseService);
        // each source assigns deterministic ids in disjoint hash-spaces
        // (HashConverter.GetHashInt31 over distinct seed strings — see
        // MangaQueueService.MapQueueItem:162-164 vs MangaPendingReleaseService.GetQueueId:674-679).
        // Before this fix the handler only called _queueService.Remove(id), which silently
        // returns when the id is absent from the in-flight static list. Pending-source ids
        // never matched, so the next GET re-included the row from the pending side and the
        // Activity Queue Remove action was a no-op.
        //
        // Dispatch order: try _queueService.Find first; if hit, route to _queueService.Remove
        // (preserves the existing in-flight removal contract incl. SignalR fan-out via
        // MangaQueueUpdatedEvent). Otherwise route to _pendingReleaseService.RemovePendingQueueItems
        // (which Deletes the matching DB row + publishes MangaPendingReleasesUpdatedEvent per
        // the Pitfall-4 wrapper at MangaPendingReleaseService.Delete:379-383). Both events are
        // re-broadcast by this controller's IHandle subscribers below.
        //
        // Mirrors canonical Sonarr QueueController.Remove dispatch at upstream
        // src/Sonarr.Api.V5/Queue/QueueController.cs:64-86 (v5-develop) which routes
        // pending-source ids to IPendingReleaseService.RemovePendingQueueItems on the
        // pending branch. Manga's simplified signature (no blocklist / skipRedownload /
        // changeCategory v1 params) is preserved.
        //
        // GH #309 (2026-06-03): the v1 "simplified Remove" signature `(int id)` silently dropped
        // the frontend's `?remove=&blocklist=&skipRedownload=&changeCategory=` query params, so the
        // Activity Queue Remove action NEVER called IDownloadClient.RemoveItem. With the in-process
        // image downloader (the only client pre-Phase-38) there was nothing to remove from an
        // external client, so the gap was invisible; the Phase-38 Manga Gateway client is the first
        // EXTERNAL client, and a removed row's gateway job survived → the row reappeared on the next
        // ~90s poll. The signature now accepts the canonical Sonarr query params and routes an
        // in-flight tracked download through the owning client's RemoveItem (mirrors
        // src/Sonarr.Api.V5/Queue/QueueController.Remove + the manga import-path eviction at
        // MangaDownloadProcessingService.cs:165). `changeCategory` is accepted for wire-compat but
        // is a no-op in v1 (manga has no post-import-category move path).
        [RestDeleteById]
        public NoContent RemoveQueueItem(int id,
                                         bool remove = true,
                                         bool blocklist = false,
                                         bool skipRedownload = false,
                                         bool changeCategory = false)
        {
            RemoveOne(id, remove, blocklist, skipRedownload);
            return TypedResults.NoContent();
        }

        // Phase 13 Plan 13-12 — F-01 gap closure (smoke-test quick-260507-p13).
        // Mirrors TV peer src/Mangarr.Api.V5/Queue/QueueController.cs:97-136 RemoveMany shape
        // (bulk DELETE belongs on the CRUD controller, NOT on QueueActionController). Manga's
        // simplified Remove signature has no blocklist/skipRedownload/changeCategory v1 params
        // (per existing [RestDeleteById] precedent at line 75-80) — Phase 15 collapse will
        // unify the signature when Tv/ deletes alongside the manga pending/blocklist plumbing.
        //
        // Phase 13 Plan 13-13 CR-02 hardening:
        //   * [Consumes("application/json")] — pinned media-type aligns with sibling manga V5
        //     endpoints (MangaQueueActionController.Grab, ChapterFileController bulk DELETE)
        //     and surfaces the request shape correctly through OpenAPI v5 doc gen. Without it,
        //     OpenAPI may emit `*/*` accept-list (breaking typed-client codegen) and form-
        //     urlencoded posts may bind `resource` as null (NRE on resource.Ids enumeration).
        //   * Null-guard on resource / resource.Ids — a missing or null body short-circuits to
        //     NoContent rather than NRE-ing inside the foreach. The TV peer's wider param surface
        //     masks this; the manga simplification reintroduces the risk.
        //   * .Distinct() before iterating — duplicate ids in the request body would otherwise
        //     fire duplicate IMangaQueueService.Remove calls, each publishing a
        //     MangaQueueUpdatedEvent and triggering a redundant SignalR Sync broadcast (UI
        //     thrash). Mirrors TV QueueController.cs:122/127 DistinctBy semantics.
        //
        // 2026-05-13 (queue-remove-pending-no-op): per-id dispatch routes pending-source ids to
        // IMangaPendingReleaseService.RemovePendingQueueItems via the shared RemoveOne helper
        // (see RemoveQueueItem above for the full rationale + canonical Sonarr peer).
        //
        // GH #309: same query-param surface as the single-item Remove above (the bulk Remove modal
        // sends the same removalOptions query string), routed through the shared RemoveOne helper.
        [HttpDelete("bulk")]
        [Consumes("application/json")]
        public NoContent RemoveMany([FromBody] QueueBulkResource resource,
                                    bool remove = true,
                                    bool blocklist = false,
                                    bool skipRedownload = false,
                                    bool changeCategory = false)
        {
            if (resource?.Ids == null)
            {
                return TypedResults.NoContent();
            }

            foreach (var id in resource.Ids.Distinct())
            {
                RemoveOne(id, remove, blocklist, skipRedownload);
            }

            return TypedResults.NoContent();
        }

        // Shared source-dispatch helper for single + bulk DELETE paths. See RemoveQueueItem
        // header comment for the canonical-Sonarr rationale + the queue-remove-pending-no-op
        // debug session.
        private void RemoveOne(int id, bool removeFromClient, bool blocklist, bool skipRedownload)
        {
            var queueItem = _queueService.Find(id);
            if (queueItem != null)
            {
                // GH #309: an in-flight row owns a real download-client job. Before dropping the
                // in-memory projection row, perform the client-side actions the user requested.
                if (removeFromClient || blocklist)
                {
                    ApplyClientSideRemoval(queueItem, removeFromClient, blocklist, skipRedownload);
                }

                _queueService.Remove(id);
                return;
            }

            // Pending-source id (or stale id absent from both projections). The pending service
            // is internally idempotent — RemovePendingQueueItems no-ops cleanly when the id
            // does not resolve to a tracked pending release. Pending releases have no download-client
            // job to delete, so removeFromClient is N/A here.
            _pendingReleaseService.RemovePendingQueueItems(id);
        }

        // GH #309: locate the owning TrackedDownload by DownloadId (the gateway jobId) and dispatch
        // the requested client-side actions. The TrackedDownload registry is owned by the Phase-36
        // MangaDownloadMonitoringService (manga has no in-memory ITrackedDownloadService cache — the
        // TV one was deleted in the Phase 15 cutover).
        private void ApplyClientSideRemoval(MangaQueueItem queueItem, bool removeFromClient, bool blocklist, bool skipRedownload)
        {
            var downloadId = queueItem.DownloadId;
            if (downloadId.IsNullOrWhiteSpace())
            {
                return;
            }

            var trackedDownload = _monitoringService.GetTrackedDownloads()
                .FirstOrDefault(t => t.DownloadItem?.DownloadId == downloadId);

            if (trackedDownload?.DownloadItem == null)
            {
                _logger.Debug("Queue Remove: no tracked download found for download id {0}; nothing to evict from client", downloadId);
                return;
            }

            if (blocklist)
            {
                Blocklist(trackedDownload, skipRedownload);
            }

            if (removeFromClient)
            {
                // Mirror MangaDownloadProcessingService.Handle(DownloadCanBeRemovedEvent):165 — resolve
                // the owning client off the factory and call RemoveItem(deleteData:true). For the
                // gateway client this issues DELETE /downloads/{jobId} (404-idempotent in the proxy).
                var downloadClient = _downloadClientFactory.GetAvailableProviders()
                    .FirstOrDefault(c => c.Definition.Id == trackedDownload.DownloadClient);

                if (downloadClient == null)
                {
                    _logger.Warn("Queue Remove: owning download client {0} is not available; cannot evict {1}", trackedDownload.DownloadClient, downloadId);
                    return;
                }

                downloadClient.RemoveItem(trackedDownload.DownloadItem, deleteData: true);
            }
        }

        // GH #309: record the release on the manga blocklist so BlocklistSpecification rejects it on
        // future searches, then mirror Sonarr QueueController.Remove's `if (!skipRedownload && blocklist)`
        // branch — search for a replacement UNLESS the user ticked "Blocklist Only" (skipRedownload).
        //
        // Block(manual: true) marks the row user-initiated so AutoRetryOrchestrator (the failure-budget
        // auto-retry) does NOT also fire — that path is for download FAILURES, not manual queue actions.
        // This makes skipRedownload the single, deterministic re-search control here, exactly like Sonarr
        // (whose BlocklistService.Block never auto-searches; the queue controller owns the search).
        //
        // Only meaningful when the tracked download resolved a RemoteChapter (matched manga); an
        // unmatched gateway job carries none, so we skip silently (nothing to blocklist or re-search).
        private void Blocklist(TrackedDownload trackedDownload, bool skipRedownload)
        {
            var remoteChapter = trackedDownload.RemoteChapter;
            var release = remoteChapter?.Release;
            if (remoteChapter?.Manga == null || release == null)
            {
                _logger.Debug("Queue Remove: blocklist requested but the tracked download has no resolved manga/release; skipping blocklist insert");
                return;
            }

            var chapterIds = remoteChapter.Chapters?.Select(c => c.Id).ToList() ?? new List<int>();

            var blocklist = new MangaBlocklist
            {
                MangaId = remoteChapter.Manga.Id,
                ChapterIds = chapterIds,
                SourceTitle = release.Title ?? trackedDownload.DownloadItem?.Title,
                SourceKey = release.Indexer,
                ReleaseGuid = release.Guid,
                ReleaseInfoJson = release.ToJson(),
                Date = DateTime.UtcNow,
                Reason = "Removed from queue",
                Source = "Manual"
            };

            _blocklistService.Block(blocklist, manual: true);

            // Sonarr-parity re-search: blocklist-and-search vs blocklist-only.
            if (!skipRedownload && chapterIds.Any())
            {
                _logger.Debug("Queue Remove: blocklisted release and re-searching {0} chapter(s)", chapterIds.Count);
                _commandQueueManager.Push(new ChapterSearchCommand(chapterIds));
            }
        }

        [NonAction]
        public void Handle(MangaQueueUpdatedEvent message)
        {
            // SignalR fan-out — every queue projection refresh emits MangaQueueUpdatedEvent
            // (Plan 06-05); the React Activity panel (Phase 7) subscribes for animated diffs.
            // ModelAction.Sync triggers a broadcast without a specific record id, signaling the
            // client to re-fetch the full list (matches TV QueueController convention).
            BroadcastResourceChange(ModelAction.Sync);
        }

        [NonAction]
        public void Handle(MangaPendingReleasesUpdatedEvent message)
        {
            // Pending-release mutations also refresh the queue projection (canonical Sonarr
            // pattern — upstream QueueController subscribes to PendingReleasesUpdatedEvent for
            // the same reason). Without this subscriber, a pending release that gets promoted
            // / held / removed would NOT re-fan SignalR to the React Activity Queue page —
            // the page would silently keep stale rows until manual refresh. Same Sync
            // broadcast shape as MangaQueueUpdatedEvent (Plan 06-05 single-channel
            // contract; MangaQueueDetailsController.cs:186-189 + MangaQueueStatusController.cs:121-125
            // sibling controllers already do this).
            BroadcastResourceChange(ModelAction.Sync);
        }
    }
}
