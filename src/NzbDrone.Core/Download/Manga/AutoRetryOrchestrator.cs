using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-12 + D-13 — see DIVERGENCE.md.
    // No exact precedent in Sonarr. Pattern composed from BlocklistService event-handler
    // shape (src/NzbDrone.Core/Blocklisting/BlocklistService.cs lines 126-158) + RESEARCH
    // Pattern 5 (lines 555-576) bounded-budget loop guard.
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
    // BOUNDED BUDGET (Pitfall 5 GUARD): D-13 IConfigService.MaxAutoRetriesPerChapter
    // (default 3) caps auto-retries per chapter. After N exhausted, chapter sits in
    // History as DownloadFailed; user manually retries from History row (HISTORY-03).
    // Without the bound, a degenerate case (every release for a chapter blocklisted)
    // could loop forever — bounded N is the safety floor.
    //
    // Phase 8 cleanup: stays as-is. No TV peer to collapse with — the auto-retry-
    // redirect-to-next-best pattern is the *arr promise applied to the manga pipeline.
    public class AutoRetryOrchestrator : IHandle<MangaBlocklistAddedEvent>
    {
        private readonly IChapterHistoryService _historyService;
        private readonly IConfigService _configService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public AutoRetryOrchestrator(
            IChapterHistoryService historyService,
            IConfigService configService,
            IManageCommandQueue commandQueueManager,
            Logger logger)
        {
            _historyService = historyService;
            _configService = configService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Handle(MangaBlocklistAddedEvent message)
        {
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

            // ── 1a. D-03 config gate — AutoRedownloadFailed (NEW; build obligation Q-D03) ─
            // The blocklist row was ALREADY inserted upstream (MangaBlocklistService.Handle, Insert
            // FIRST). This gate suppresses ONLY the re-search — blocklisting still always fires.
            // Canonical placement mirrors v5-develop:Download/RedownloadFailedDownloadService.Handle
            // (early-return on !AutoRedownloadFailed before any search command push). Respects the
            // EXISTING IConfigService.AutoRedownloadFailed setting (default true); adds NO new setting.
            if (!_configService.AutoRedownloadFailed)
            {
                _logger.Info(
                    "AutoRedownloadFailed is off; skipping auto-retry re-search for chapter {0} (blocklist still applied)",
                    chapterId);
                return;
            }

            // ── 2. Count prior DownloadFailed history rows (Pattern 5 / D-13 budget) ────
            // BL-01 GUARD: FindByChapterId queries ChapterHistory.ChapterId — NOT
            // EpisodeHistory.EpisodeId (independent autoincrement spaces; cross-domain
            // collision was the Phase 5 surprise this skill class catches).
            var failureCount = _historyService.FindByChapterId(chapterId)
                .Count(h => h.EventType == ChapterHistoryEventType.DownloadFailed);
            var max = _configService.MaxAutoRetriesPerChapter;

            // ── 3. Bounded budget gate ──────────────────────────────────────────────────
            if (failureCount >= max)
            {
                _logger.Info(
                    "Chapter {0} exhausted {1} auto-retries; user must manually retry from History",
                    chapterId,
                    max);
                return;
            }

            // ── 4. Fire ChapterSearchCommand for the just-failed chapter ────────────────
            // The blocklist row is COMMITTED at this point (ordering contract enforced by
            // Plan 06-04's MangaBlocklistService.Handle: Insert FIRST, then PublishEvent).
            // BlocklistSpecification.IsSatisfiedBy will reject release A on the next
            // decision pass; the ranked next-best release is grabbed instead.
            _logger.Info(
                "Chapter {0} auto-retry attempt {1}/{2}; firing ChapterSearchCommand",
                chapterId,
                failureCount + 1,
                max);
            _commandQueueManager.Push(new ChapterSearchCommand(new List<int> { chapterId }));
        }
    }
}
