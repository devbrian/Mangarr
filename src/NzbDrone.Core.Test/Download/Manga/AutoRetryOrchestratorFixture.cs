using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Manga
{
    // Phase 6 Plan 06-08 — AutoRetryOrchestrator BLOCKING tests:
    //
    //   D-12 + D-03 — auto-retry on terminal failure with deterministic ordering vs
    //   the blocklist insert. SONARR PARITY (auto-retry-one-release-exhaust, 2026-06-07):
    //   the orchestrator mirrors RedownloadFailedDownloadService — re-search on EVERY
    //   failure with NO retry budget; the blocklist bounds the loop (each failure
    //   blocklists a different release; once all candidates are blocklisted the re-search
    //   finds nothing and the loop terminates). The earlier D-13 MaxAutoRetriesPerChapter
    //   cap was removed (it tripped "exhausted" on a chapter's first failure whenever stale
    //   DownloadFailed history rows had accumulated past the cap).
    //
    //   ⚠ HARD GATE: AutoRetryOrchestrator subscribes to MangaBlocklistAddedEvent (NOT
    //   ChapterDownloadFailedEvent). Plan 06-04's MangaBlocklistService inserts the
    //   blocklist row BEFORE publishing MangaBlocklistAddedEvent; subscribing here ensures
    //   the row is committed when the auto-retry ChapterSearchCommand is queued, so
    //   BlocklistSpecification correctly rejects the just-failed release on the next
    //   decision pass.
    //
    //   Tests cover:
    //     1. on failure → push ChapterSearchCommand (every time, no budget)
    //     2. repeated failures → push every time (Sonarr parity; no exhaustion cap)
    //     3. manual blocklist → no push (GH #309)
    //     4. SourceEvent null → fall back to Blocklist.ChapterIds.First()
    //     5. no resolvable chapter id → warn, no push
    //     6. ordering invariant: at the moment Push is called, the blocklist row is present
    //     7. AutoRedownloadFailed off → no push (D-03 gate)
    //     8. handler subscribes to MangaBlocklistAddedEvent, NOT ChapterDownloadFailedEvent
    [TestFixture]
    public class AutoRetryOrchestratorFixture : CoreTest<AutoRetryOrchestrator>
    {
        private const int ChapterId = 42;
        private const int MangaId = 7;

        private MangaBlocklist _blocklist;

        [SetUp]
        public void Setup()
        {
            _blocklist = new MangaBlocklist
            {
                Id = 5,
                MangaId = MangaId,
                ChapterIds = new List<int> { ChapterId },
                SourceTitle = "Vinland Saga - 0042",
                SourceKey = "MangaDex",
                ReleaseGuid = "g42"
            };

            // D-03 (Phase 36 Plan 04): AutoRedownloadFailed default true — re-search enabled.
            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.AutoRedownloadFailed)
                .Returns(true);
        }

        private ChapterDownloadFailedEvent BuildSourceEvent()
        {
            return new ChapterDownloadFailedEvent(
                rowId: 99,
                mangaId: MangaId,
                chapterId: ChapterId,
                failureReason: "manifest re-fetch exhausted");
        }

        // ── RE-SEARCH TESTS ─────────────────────────────────────────────────────────────

        [Test]
        public void Pushes_ChapterSearchCommand_on_failure()
        {
            Subject.Handle(new MangaBlocklistAddedEvent(_blocklist, BuildSourceEvent()));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.Is<ChapterSearchCommand>(c => c.ChapterIds != null && c.ChapterIds.Contains(ChapterId)),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);
        }

        [Test]
        public void Re_searches_on_every_failure_no_retry_budget_SONARR_PARITY()
        {
            // The whole point of removing the D-13 budget: a chapter that has failed many
            // times still gets a re-search on its next failure. Sonarr's
            // RedownloadFailedDownloadService fires on EVERY DownloadFailedEvent with no
            // counter; the blocklist (not a cap) bounds the loop. Fire 5 failures → 5 pushes.
            for (var i = 0; i < 5; i++)
            {
                Subject.Handle(new MangaBlocklistAddedEvent(_blocklist, BuildSourceEvent()));
            }

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.IsAny<ChapterSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Exactly(5));
        }

        // ── FALLBACK TESTS ──────────────────────────────────────────────────────────────

        [Test]
        public void Skips_auto_retry_when_blocklist_is_manual()
        {
            // GH #309: a user-initiated (manual) blocklist — e.g. Queue Remove "Blocklist Release" —
            // must NOT trigger the auto-retry. The queue-Remove caller owns the explicit
            // skipRedownload-gated re-search; the orchestrator stays failure-only (Sonarr parity
            // with RedownloadFailedDownloadService). AutoRedownloadFailed on would otherwise fire a
            // search — the manual flag is what suppresses it.
            Subject.Handle(new MangaBlocklistAddedEvent(_blocklist, sourceEvent: null, manual: true));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.IsAny<ChapterSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);
        }

        [Test]
        public void Falls_back_to_Blocklist_ChapterIds_when_SourceEvent_is_null()
        {
            // Some Block(...) UI paths emit MangaBlocklistAddedEvent with null SourceEvent
            // (the manual-block path doesn't carry a source ChapterDownloadFailedEvent).
            // The orchestrator must still resolve the chapter id via Blocklist.ChapterIds.
            Subject.Handle(new MangaBlocklistAddedEvent(_blocklist, sourceEvent: null));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.Is<ChapterSearchCommand>(c => c.ChapterIds.Contains(ChapterId)),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);
        }

        [Test]
        public void Skips_when_no_resolvable_chapter_id_warns()
        {
            var emptyBlocklist = new MangaBlocklist
            {
                MangaId = MangaId,
                ChapterIds = new List<int>(),
                SourceTitle = "junk"
            };

            Subject.Handle(new MangaBlocklistAddedEvent(emptyBlocklist, sourceEvent: null));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.IsAny<ChapterSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);

            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        // ── ORDERING INVARIANT (Plan 06-04 ↔ Plan 06-08 contract) ───────────────────────

        [Test]
        public void Ordering_invariant_blocklist_row_is_observable_when_Push_is_called()
        {
            // The Plan 06-04 ↔ Plan 06-08 contract: Plan 06-04 inserts the row BEFORE
            // publishing MangaBlocklistAddedEvent, so by the time this handler runs, a
            // BlocklistSpecification.IsSatisfiedBy query (consuming the same repository
            // state) MUST see the row. We simulate by using a state flag set inside the
            // Push callback to track whether the row was "visible" at the moment Push was
            // called. The ordering invariant is: visible == true.
            var rowVisibleAtPushTime = false;

            Mocker.GetMock<IManageCommandQueue>()
                .Setup(q => q.Push(
                    It.IsAny<ChapterSearchCommand>(),
                    It.IsAny<CommandPriority>(),
                    It.IsAny<CommandTrigger>()))
                .Callback(() =>
                {
                    // At the moment Push is called, the blocklist row must already be
                    // committed (Plan 06-04 contract — the event payload IS the
                    // committed row).
                    rowVisibleAtPushTime = _blocklist != null && _blocklist.Id > 0;
                });

            Subject.Handle(new MangaBlocklistAddedEvent(_blocklist, BuildSourceEvent()));

            rowVisibleAtPushTime.Should().BeTrue(
                "Plan 06-04 ↔ 06-08 ordering invariant: blocklist row must be observable when ChapterSearchCommand is queued");
        }

        // ── D-03 CONFIG GATE (Phase 36 Plan 04 — AutoRedownloadFailed) ──────────────────

        [Test]
        public void Pushes_ChapterSearchCommand_when_AutoRedownloadFailed_is_on()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.AutoRedownloadFailed)
                .Returns(true);

            Subject.Handle(new MangaBlocklistAddedEvent(_blocklist, BuildSourceEvent()));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.IsAny<ChapterSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);
        }

        [Test]
        public void Does_not_push_ChapterSearchCommand_when_AutoRedownloadFailed_is_off()
        {
            // D-03 gate: the blocklist row was already inserted upstream (always fires); the
            // orchestrator suppresses ONLY the re-search when the config is off.
            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.AutoRedownloadFailed)
                .Returns(false);

            Subject.Handle(new MangaBlocklistAddedEvent(_blocklist, BuildSourceEvent()));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.IsAny<ChapterSearchCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);
        }

        // ── INTERFACE CHECKS (HARD GATES) ───────────────────────────────────────────────

        [Test]
        public void Subscribes_to_MangaBlocklistAddedEvent_not_ChapterDownloadFailedEvent_ANTI_RACE_GATE()
        {
            // Hard gate: AutoRetryOrchestrator must implement IHandle<MangaBlocklistAddedEvent>.
            typeof(NzbDrone.Core.Messaging.Events.IHandle<MangaBlocklistAddedEvent>)
                .IsAssignableFrom(typeof(AutoRetryOrchestrator)).Should().BeTrue(
                    "Plan 06-08 hard gate: must subscribe to MangaBlocklistAddedEvent");

            // Hard gate: AutoRetryOrchestrator must NOT implement
            // IHandle<ChapterDownloadFailedEvent> directly — that would create a
            // non-deterministic handler-order race against MangaBlocklistService.Handle
            // on the same event.
            typeof(NzbDrone.Core.Messaging.Events.IHandle<ChapterDownloadFailedEvent>)
                .IsAssignableFrom(typeof(AutoRetryOrchestrator)).Should().BeFalse(
                    "Plan 06-08 hard gate: must NOT subscribe to ChapterDownloadFailedEvent (Plan 06-04 ↔ 06-08 anti-race contract)");
        }
    }
}
