using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.IndexerSearchTests.Manga
{
    // Phase 6 Plan 06-06 Task 3 — MangaRssSyncService Http filter + per-indexer SyncInterval.
    [TestFixture]
    public class MangaRssSyncServiceFixture : CoreTest<MangaRssSyncService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.MangaRssSyncInterval).Returns(15);

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                .Returns(new List<MangaDownloadDecision>());

            // Debug-session queue-items-not-downloading (2026-05-13) — default pending-queue + process pipeline mocks.
            Mocker.GetMock<IMangaPendingReleaseService>()
                .Setup(s => s.GetPending())
                .Returns(new List<ReleaseInfo>());

            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                .Setup(p => p.ProcessDecisions(It.IsAny<List<MangaDownloadDecision>>()))
                .ReturnsAsync(new ProcessedMangaDecisions(
                    new List<MangaDownloadDecision>(),
                    new List<MangaDownloadDecision>(),
                    new List<MangaDownloadDecision>()));
        }

        private Mock<IIndexer> BuildHttpIndexer(
            int id,
            string name,
            IList<ReleaseInfo> result = null,
            Exception throws = null,
            int syncInterval = 0,
            DateTime? lastRssSync = null)
        {
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Protocol).Returns(DownloadProtocol.Http);
            indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition
            {
                Id = id,
                Name = name,
                SyncInterval = syncInterval,
                LastRssSync = lastRssSync
            });
            if (throws != null)
            {
                indexer.Setup(i => i.FetchRecent()).ThrowsAsync(throws);
            }
            else
            {
                indexer.Setup(i => i.FetchRecent()).ReturnsAsync(result ?? new List<ReleaseInfo>());
            }

            return indexer;
        }

        private Mock<IIndexer> BuildUsenetIndexer(int id, string name)
        {
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Protocol).Returns(DownloadProtocol.Unknown);
            indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition { Id = id, Name = name });
            indexer.Setup(i => i.FetchRecent()).ReturnsAsync(new List<ReleaseInfo>());
            return indexer;
        }

        [Test]
        public void Execute_filters_to_http_protocol_indexers_only()
        {
            var http = BuildHttpIndexer(1, "MangaDex");
            var usenet = BuildUsenetIndexer(2, "Newznab");

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { http.Object, usenet.Object });

            Subject.Execute(new MangaRssSyncCommand());

            http.Verify(i => i.FetchRecent(), Times.Once);
            usenet.Verify(i => i.FetchRecent(), Times.Never);
        }

        [Test]
        public void Execute_isolates_one_failing_indexer_so_batch_continues()
        {
            var good = BuildHttpIndexer(1, "MangaDex", new List<ReleaseInfo> { new() { Title = "Good 001", Guid = "g1" } });
            var bad = BuildHttpIndexer(2, "Comix", throws: new InvalidOperationException("simulated indexer crash"));

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { good.Object, bad.Object });

            Subject.Execute(new MangaRssSyncCommand());

            good.Verify(i => i.FetchRecent(), Times.Once);
            bad.Verify(i => i.FetchRecent(), Times.Once);
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void Execute_honors_per_indexer_sync_interval_override_and_skips_when_not_due()
        {
            // Indexer override = 30min; LastRssSync was 15min ago — NOT due.
            var notDue = BuildHttpIndexer(
                1,
                "MangaDex",
                syncInterval: 30,
                lastRssSync: DateTime.UtcNow.AddMinutes(-15));

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { notDue.Object });

            Subject.Execute(new MangaRssSyncCommand());

            notDue.Verify(i => i.FetchRecent(), Times.Never);
        }

        [Test]
        public void Execute_per_indexer_override_due_when_elapsed_exceeds_override()
        {
            // Indexer override = 5min; LastRssSync was 10min ago — due.
            var due = BuildHttpIndexer(
                1,
                "MangaDex",
                syncInterval: 5,
                lastRssSync: DateTime.UtcNow.AddMinutes(-10));

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { due.Object });

            Subject.Execute(new MangaRssSyncCommand());

            due.Verify(i => i.FetchRecent(), Times.Once);
        }

        [Test]
        public void Execute_treats_never_synced_indexer_as_due()
        {
            var fresh = BuildHttpIndexer(1, "MangaDex", lastRssSync: null);

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { fresh.Object });

            Subject.Execute(new MangaRssSyncCommand());

            fresh.Verify(i => i.FetchRecent(), Times.Once);
        }

        [Test]
        public void Execute_pipes_aggregated_reports_into_decision_maker()
        {
            var indexer = BuildHttpIndexer(1, "MangaDex", new List<ReleaseInfo>
            {
                new() { Title = "A", Guid = "g1" },
                new() { Title = "B", Guid = "g2" }
            });

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Verify(d => d.GetRssDecision(It.Is<List<ReleaseInfo>>(r => r.Count == 2), It.IsAny<bool>()),
                    Times.Once);
        }

        // ---------------- Phase 9 Plan 09-12 — gap-01 + gap-02 close-out ----------------

        [Test]
        public void Execute_should_publish_MangaRssSyncCompleteEvent_exactly_once_per_run()
        {
            // gap-01: Plan 09-10 IHandle<MangaRssSyncCompleteEvent> wiring depends on this publish.
            var decisions = new List<MangaDownloadDecision>();
            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                .Returns(decisions);

            var indexer = BuildHttpIndexer(1, "MangaDex");
            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.Is<MangaRssSyncCompleteEvent>(
                        evt => evt.ProcessedDecisions != null
                               && ReferenceEquals(evt.ProcessedDecisions, decisions))),
                    Times.Once);
        }

        [Test]
        public void Execute_should_NOT_publish_when_no_indexers_due_for_refresh()
        {
            // Empty indexer set short-circuits before publish — no event emitted on idle ticks.
            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer>());

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<MangaRssSyncCompleteEvent>()), Times.Never);
        }

        [Test]
        public void FetchIndexerSafe_should_persist_LastRssSync_on_successful_FetchRecent()
        {
            // gap-02: per-indexer SyncInterval override (D-07) reads LastRssSync; without
            // this write the override branch is unreachable in practice.
            var indexer = BuildHttpIndexer(1, "MangaDex", new List<ReleaseInfo>());

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            var beforeRun = DateTime.UtcNow.AddSeconds(-1);

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IIndexerFactory>()
                .Verify(
                    f => f.Update(It.Is<IndexerDefinition>(
                        d => d.Id == 1
                             && d.LastRssSync.HasValue
                             && d.LastRssSync.Value >= beforeRun
                             && d.LastRssSync.Value <= DateTime.UtcNow.AddSeconds(1))),
                    Times.Once);
        }

        [Test]
        public void FetchIndexerSafe_should_NOT_persist_LastRssSync_when_FetchRecent_throws()
        {
            // gap-02: a thrown FetchRecent must NOT leave a stale timestamp on disk.
            // The catch block returns Array.Empty<ReleaseInfo>() and the LastRssSync write
            // (which lives INSIDE try AFTER await) never executes.
            var indexer = BuildHttpIndexer(
                1,
                "MangaDex",
                throws: new InvalidOperationException("simulated indexer crash"));

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IIndexerFactory>()
                .Verify(f => f.Update(It.IsAny<IndexerDefinition>()), Times.Never);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void Pitfall_4_ordering_LastRssSync_write_completes_BEFORE_event_publish()
        {
            // Pitfall 4 contract: per-indexer DB write FIRST (LastRssSync inside
            // FetchIndexerSafe), batch event LAST (PublishEvent at end of Execute).
            // MockSequence asserts strict source-order across the two unrelated mocks —
            // if the order is inverted, the second InSequence Setup throws on its first call.
            var sequence = new MockSequence();

            Mocker.GetMock<IIndexerFactory>()
                .InSequence(sequence)
                .Setup(f => f.Update(It.IsAny<IndexerDefinition>()));

            Mocker.GetMock<IEventAggregator>()
                .InSequence(sequence)
                .Setup(e => e.PublishEvent(It.IsAny<MangaRssSyncCompleteEvent>()));

            var indexer = BuildHttpIndexer(1, "MangaDex", new List<ReleaseInfo>());

            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            // Both calls must have happened; sequence enforces the ordering.
            Mocker.GetMock<IIndexerFactory>()
                .Verify(f => f.Update(It.IsAny<IndexerDefinition>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<MangaRssSyncCompleteEvent>()), Times.Once);
        }

        // ---------------- Debug-session queue-items-not-downloading (2026-05-13) — CONCAT-PENDING + GRAB-DECISIONS ----------------

        [Test]
        public void Execute_should_concat_pending_releases_into_reports_before_decision_making()
        {
            // CONCAT-PENDING: mirror TV RssSyncService.Sync() lines 41-43. Pending releases
            // (e.g. previously queued with PendingReleaseReason.DownloadClientUnavailable)
            // must be re-fed into the decision-maker on every RSS tick so the underlying
            // blocker can clear and the release can be grabbed on the next pass.
            var fresh = new List<ReleaseInfo>
            {
                new() { Title = "Fresh-A", Guid = "fresh-a" },
                new() { Title = "Fresh-B", Guid = "fresh-b" }
            };
            var pending = new List<ReleaseInfo>
            {
                new() { Title = "Pending-X", Guid = "pending-x", PendingReleaseReason = PendingReleaseReason.DownloadClientUnavailable },
                new() { Title = "Pending-Y", Guid = "pending-y", PendingReleaseReason = PendingReleaseReason.Delay }
            };

            var indexer = BuildHttpIndexer(1, "MangaDex", fresh);
            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Mocker.GetMock<IMangaPendingReleaseService>()
                .Setup(s => s.GetPending())
                .Returns(pending);

            Subject.Execute(new MangaRssSyncCommand());

            // Decision-maker should see all 4 reports (2 fresh + 2 pending).
            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Verify(
                    d => d.GetRssDecision(
                        It.Is<List<ReleaseInfo>>(r =>
                            r.Count == 4
                            && r.Exists(x => x.Guid == "fresh-a")
                            && r.Exists(x => x.Guid == "fresh-b")
                            && r.Exists(x => x.Guid == "pending-x")
                            && r.Exists(x => x.Guid == "pending-y")),
                        It.IsAny<bool>()),
                    Times.Once);
        }

        [Test]
        public void Execute_should_hand_decisions_to_ProcessDecisions_grab_pipeline()
        {
            // GRAB-DECISIONS: mirror TV RssSyncService.Sync() line 44. Without this call the
            // decision list is discarded — RSS finds releases but never grabs them. This is
            // the actual user-visible symptom in debug-session queue-items-not-downloading.
            var decisions = new List<MangaDownloadDecision> { new MangaDownloadDecision(null) };
            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                .Returns(decisions);

            var indexer = BuildHttpIndexer(1, "MangaDex");
            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                .Verify(p => p.ProcessDecisions(It.Is<List<MangaDownloadDecision>>(d => ReferenceEquals(d, decisions))),
                    Times.Once);
        }

        [Test]
        public void Execute_should_still_publish_MangaRssSyncCompleteEvent_after_grab_pipeline_runs()
        {
            // The event payload remains the pre-process decision list so
            // MangaPendingReleaseService.Handle(MangaRssSyncCompleteEvent) can still
            // filter `.Rejected` to prune stale pending rows (Plan 09-10 wiring).
            var decisions = new List<MangaDownloadDecision>();
            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                .Returns(decisions);

            var indexer = BuildHttpIndexer(1, "MangaDex");
            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                .Verify(p => p.ProcessDecisions(It.IsAny<List<MangaDownloadDecision>>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.Is<MangaRssSyncCompleteEvent>(
                        evt => ReferenceEquals(evt.ProcessedDecisions, decisions))),
                    Times.Once);
        }

        // ---------------- Phase 40 RSS self-heal — synthesize uncataloged chapters from /recent ----------------

        private static MangaDownloadDecision DecisionForManga(int mangaId, string title)
        {
            return new MangaDownloadDecision(new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = mangaId, Title = title }
            });
        }

        [Test]
        public void Execute_should_synthesize_chapters_once_per_resolved_manga()
        {
            // RSS has no single search target, so the decisions are grouped by their resolved
            // RemoteChapter.Manga and synthesized one manga at a time.
            var decisions = new List<MangaDownloadDecision>
            {
                DecisionForManga(7, "Solo Leveling"),
                DecisionForManga(7, "Solo Leveling"),
                DecisionForManga(10, "Solo Leveling: Ragnarok"),
            };

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                .Returns(decisions);

            var indexer = BuildHttpIndexer(1, "MangaDex");
            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            // Manga 7 — both decisions grouped into a single call.
            Mocker.GetMock<IChapterSynthesisService>()
                .Verify(s => s.SynthesizeFromDecisions(
                    It.Is<NzbDrone.Core.Manga.Manga>(m => m.Id == 7),
                    It.Is<List<MangaDownloadDecision>>(d => d.Count == 2)),
                    Times.Once);

            // Manga 10 — single decision, its own call.
            Mocker.GetMock<IChapterSynthesisService>()
                .Verify(s => s.SynthesizeFromDecisions(
                    It.Is<NzbDrone.Core.Manga.Manga>(m => m.Id == 10),
                    It.Is<List<MangaDownloadDecision>>(d => d.Count == 1)),
                    Times.Once);
        }

        [Test]
        public void Execute_should_skip_synthesis_for_decisions_with_no_resolved_manga()
        {
            // UnknownManga decisions (GetManga returned null → RemoteChapter.Manga == null, or
            // a null RemoteChapter entirely) must never reach synthesis — there is no manga to
            // attribute the release to.
            var decisions = new List<MangaDownloadDecision>
            {
                new MangaDownloadDecision(new RemoteChapter { Manga = null }),
                new MangaDownloadDecision(null),
            };

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                .Returns(decisions);

            var indexer = BuildHttpIndexer(1, "MangaDex");
            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IChapterSynthesisService>()
                .Verify(s => s.SynthesizeFromDecisions(
                    It.IsAny<NzbDrone.Core.Manga.Manga>(),
                    It.IsAny<List<MangaDownloadDecision>>()),
                    Times.Never);
        }

        [Test]
        public void Execute_should_isolate_synthesis_failure_and_still_grab_and_publish()
        {
            // Best-effort: synthesis is a pure side-effect — a throw must not abort the RSS
            // tick or swallow the decisions the grab pipeline + complete-event are waiting for.
            var decisions = new List<MangaDownloadDecision> { DecisionForManga(7, "Solo Leveling") };

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                .Returns(decisions);

            Mocker.GetMock<IChapterSynthesisService>()
                .Setup(s => s.SynthesizeFromDecisions(
                    It.IsAny<NzbDrone.Core.Manga.Manga>(),
                    It.IsAny<List<MangaDownloadDecision>>()))
                .Throws(new InvalidOperationException("simulated synthesis crash"));

            var indexer = BuildHttpIndexer(1, "MangaDex");
            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                .Verify(p => p.ProcessDecisions(It.Is<List<MangaDownloadDecision>>(d => ReferenceEquals(d, decisions))),
                    Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<MangaRssSyncCompleteEvent>()), Times.Once);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Execute_should_synthesize_BEFORE_handing_decisions_to_grab_pipeline()
        {
            // Catalog backfill must happen before the grab pipeline so the synthesized rows
            // are wanted for the next RSS tick / Missing sweep. MockSequence asserts strict
            // source-order: synthesis FIRST, ProcessDecisions SECOND.
            var sequence = new MockSequence();

            Mocker.GetMock<IChapterSynthesisService>()
                .InSequence(sequence)
                .Setup(s => s.SynthesizeFromDecisions(
                    It.IsAny<NzbDrone.Core.Manga.Manga>(),
                    It.IsAny<List<MangaDownloadDecision>>()));

            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                .InSequence(sequence)
                .Setup(p => p.ProcessDecisions(It.IsAny<List<MangaDownloadDecision>>()))
                .ReturnsAsync(new ProcessedMangaDecisions(
                    new List<MangaDownloadDecision>(),
                    new List<MangaDownloadDecision>(),
                    new List<MangaDownloadDecision>()));

            var decisions = new List<MangaDownloadDecision> { DecisionForManga(7, "Solo Leveling") };
            Mocker.GetMock<IMakeMangaDownloadDecision>()
                .Setup(d => d.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                .Returns(decisions);

            var indexer = BuildHttpIndexer(1, "MangaDex");
            Mocker.GetMock<IIndexerFactory>()
                .Setup(f => f.RssEnabled(true))
                .Returns(new List<IIndexer> { indexer.Object });

            Subject.Execute(new MangaRssSyncCommand());

            Mocker.GetMock<IChapterSynthesisService>()
                .Verify(s => s.SynthesizeFromDecisions(
                    It.IsAny<NzbDrone.Core.Manga.Manga>(),
                    It.IsAny<List<MangaDownloadDecision>>()),
                    Times.Once);
            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                .Verify(p => p.ProcessDecisions(It.IsAny<List<MangaDownloadDecision>>()), Times.Once);
        }
    }
}
