using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-12 — see DIVERGENCE.md.
    // Models Sonarr's RedownloadFailedDownloadService (src/NzbDrone.Core/Download/
    // RedownloadFailedDownloadService.cs): on a failed grab, re-search so the next-best
    // release is grabbed instead. Pattern composed from BlocklistService event-handler
    // shape (src/NzbDrone.Core/Blocklisting/BlocklistService.cs lines 126-158).
    //
    // ============================================================================
    // EVENT-ORDERING CONTRACT (Plan 06-08 hard-gate; revised from original D-12 framing):
    //
    //   Subscribe to MangaBlocklistAddedEvent (Plan 06-04 emits this AFTER
    //   _repository.Insert(blocklist) returns). Synchronous IEventAggregator
    //   fan-out guarantees the row is COMMITTED before this handler runs.
    //
    //   When this handler then pushes ChapterSearchCommand, the new search command
    //   (eventually executed asynchronously via the command queue) runs
    //   BlocklistSpecification.IsSatisfiedBy against a repository state that
    //   already contains the just-blocklisted row — release A is rejected and
    //   the next-best release is chosen.
    //
    //   This post-Insert subscription is Mangarr's equivalent of Sonarr's
    //   [EventHandleOrder(EventHandleOrder.Last)] on RedownloadFailedDownloadService —
    //   both exist to guarantee the blocklist row is observable before the re-search.
    //
    //   ⚠ HARD GATE — DO NOT subscribe directly to the upstream
    //   ChapterDownloadFailedEvent emitted by Phase 4 — handler order on the SAME event
    //   is non-deterministic in Sonarr's IEventAggregator, so a direct subscription
    //   would race against MangaBlocklistService.Handle (which inserts the row in
    //   response to the same upstream event). The race could leave
    //   BlocklistSpecification looking at an uncommitted state and re-grabbing the
    //   just-failed release. AutoRetryOrchestratorFixture asserts via reflection
    //   that the IHandle interface for the upstream event is NOT implemented here.
    // ============================================================================
    //
    // D-03 GATE (Phase 36 Plan 04 — Q-D03 build obligation): Handle early-returns when
    // IConfigService.AutoRedownloadFailed is off, suppressing ONLY the re-search. The blocklist
    // row is inserted UPSTREAM by MangaBlocklistService (Insert FIRST), so blocklisting always
    // fires regardless of this setting. Canonical placement mirrors v5-develop:Download/
    // RedownloadFailedDownloadService.Handle. Respects the EXISTING AutoRedownloadFailed setting
    // (default true); no new setting is added.
    //
    // NO RETRY BUDGET (Sonarr parity — auto-retry-one-release-exhaust, 2026-06-07):
    // The earlier D-13 MaxAutoRetriesPerChapter budget was REMOVED. Sonarr's
    // RedownloadFailedDownloadService.Handle re-searches on EVERY DownloadFailedEvent with no
    // counter — the loop is bounded NATURALLY by the blocklist: each failure blocklists a
    // DIFFERENT release, so the next-best ranks up; once every candidate for the chapter is
    // blocklisted the re-search finds nothing acceptable, nothing is grabbed, no new failure
    // fires, and the loop terminates. A lifetime failed-row counter (the prior implementation)
    // instead tripped "exhausted" on a chapter's first observed failure whenever stale/duplicate
    // DownloadFailed history rows had accumulated past the cap — suppressing the re-search that is
    // the whole point of the *arr promise. Do NOT reintroduce a per-chapter retry cap; trust the
    // blocklist to bound the loop exactly as Sonarr does.
    //
    // Phase 8 cleanup: stays as-is. No TV peer to collapse with beyond the
    // RedownloadFailedDownloadService shape it already mirrors.
    public class AutoRetryOrchestrator : IHandle<MangaBlocklistAddedEvent>
    {
        private readonly IConfigService _configService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public AutoRetryOrchestrator(
            IConfigService configService,
            IManageCommandQueue commandQueueManager,
            Logger logger)
        {
            _configService = configService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Handle(MangaBlocklistAddedEvent message)
        {
            // ── 0. Manual blocklist gate (GH #309) ──────────────────────────────────────
            // A user-initiated blocklist (e.g. the Activity Queue Remove modal's "Blocklist
            // Release" checkbox) is NOT a download-failure auto-retry trigger. Sonarr decouples
            // these: RedownloadFailedDownloadService fires only on DownloadFailedEvent, while
            // QueueController.Remove owns its own re-search via the per-action skipRedownload flag.
            // Mirror that here — skip the auto-retry for manual blocklists so the queue-Remove
            // caller's skipRedownload choice is the SINGLE, deterministic re-search control. The
            // on-failure path (SourceEvent set, Manual=false) is unaffected.
            if (message.Manual)
            {
                return;
            }

            // ── 1. Identify the chapter that just failed ────────────────────────────────
            // Prefer the source ChapterDownloadFailedEvent for the richest context;
            // fall back to the blocklist row's ChapterIds (single-chapter releases will
            // have exactly one entry per Plan 06-04's auto-blocklist Handle path).
            var chapterId = message.SourceEvent?.ChapterId
                ?? message.Blocklist?.ChapterIds?.FirstOrDefault()
                ?? 0;
            if (chapterId == 0)
            {
                _logger.Warn("MangaBlocklistAddedEvent fired without a resolvable chapter id; auto-retry skipped");
                return;
            }

            // Human-readable label: the release SourceTitle (e.g. "[EZManga] The Forgotten
            // Field - Chapter 13 [en]") carries the chapter NUMBER the user recognizes; the bare
            // chapterId is the DB PK and reads as a wrong-chapter conflation in the log. Keep the
            // id for DB correlation but lead with the title when available. No IChapterService
            // lookup — that would add a ModelNotFoundException-throw risk on the failure path.
            var sourceTitle = message.SourceEvent?.SourceTitle ?? message.Blocklist?.SourceTitle;
            var chapterLabel = sourceTitle.IsNotNullOrWhiteSpace()
                ? string.Format("'{0}' (chapterId {1})", sourceTitle, chapterId)
                : string.Format("chapterId {0}", chapterId);

            // ── 2. D-03 config gate — AutoRedownloadFailed (build obligation Q-D03) ──────
            // The blocklist row was ALREADY inserted upstream (MangaBlocklistService.Handle, Insert
            // FIRST). This gate suppresses ONLY the re-search — blocklisting still always fires.
            // Canonical placement mirrors v5-develop:Download/RedownloadFailedDownloadService.Handle
            // (early-return on !AutoRedownloadFailed before any search command push). Respects the
            // EXISTING IConfigService.AutoRedownloadFailed setting (default true); adds NO new setting.
            if (!_configService.AutoRedownloadFailed)
            {
                _logger.Info(
                    "AutoRedownloadFailed is off; skipping auto-retry re-search for {0} (blocklist still applied)",
                    chapterLabel);
                return;
            }

            // ── 3. Fire ChapterSearchCommand for the just-failed chapter ────────────────
            // The blocklist row is COMMITTED at this point (ordering contract enforced by
            // Plan 06-04's MangaBlocklistService.Handle: Insert FIRST, then PublishEvent).
            // BlocklistSpecification.IsSatisfiedBy will reject the just-failed release on the
            // next decision pass; the ranked next-best release is grabbed instead. No retry
            // budget — the blocklist bounds the loop (Sonarr parity; see class header).
            _logger.Info("Auto-retry for {0}; firing ChapterSearchCommand for next-best release", chapterLabel);
            _commandQueueManager.Push(new ChapterSearchCommand(new List<int> { chapterId }));
        }
    }
}
