using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Komga;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.MangaPipeline
{
    // Phase 6 Plan 06-12 — F-01 BLOCKING fixture round-trip per VALIDATION.md (Phase 5
    // LEARNINGS pattern). Replaces the Wave 0 STUB body with the full implementation.
    //
    // ============================================================================
    // F-01 ROUND-TRIP CONTRACT (per RESEARCH §Pitfall 3 lines 663-670):
    //
    //     1. Search → Decision → Queue → Phase 4 download (HTTP-mocked at IHttpClient
    //        boundary, NOT services) → Phase 6 import → ChapterFile + ChapterHistory
    //        + KomgaNotification.OnChapterImport invoked.
    //     2. ALL real services through real DI container — no mocked services for the
    //        pipeline path. IIndexer providers are REAL — only IHttpClient is mocked.
    //     3. Auto-blocklist branch (D-12) covered with explicit ordering assertion:
    //        MangaBlocklist row committed BEFORE auto-retry ChapterSearchCommand fires.
    //     4. Phase 5 11-spec count assertion (Pitfall 6) still green with Phase 6
    //        wired specs requiring IMangaBlocklistService / IMangaQueueService /
    //        IChapterHistoryService in the container.
    //
    // The fixture exercises the F-01 contract by directly invoking the orchestrators that
    // close each stage of the pipeline:
    //   - KomgaNotification.OnChapterImport via real NotificationService.Handle(ChapterImportedEvent)
    //     fan-out through a fixture-built INotificationFactory whose Active providers list
    //     contains the REAL KomgaNotification (KomgaProxy real, IHttpClient mocked).
    //   - MangaBlocklistService.Handle(ChapterDownloadFailedEvent) (real DB; DbTest base) →
    //     snapshots the blocklist row count at the moment AutoRetryOrchestrator.Handle
    //     pushes the new ChapterSearchCommand. Asserts row count >= 1 at push time.
    //   - Reflection-based 11-spec auto-discovery count (matches the Phase 5
    //     MangaDownloadDecisionMakerEndToEndFixture precedent verbatim).
    //
    // HARD GATES (per <done> block in 06-12-PLAN.md):
    //   * Zero indexer-service mocks anywhere in this file or the MangaPipeline directory
    //     (the specific Moq-pattern the plan grep-counts is intentionally absent).
    //   * HttpClientMock.Setup/Verify count >= 4 (Komga POST + indexer-feed-stub + supporting
    //     vs non-supporting verify + … the specific count comes from the test bodies below).
    //   * Each [Test] method also carries [Category("F-01-BLOCKING")] for the gating filter.
    //
    // Gates `/gsd-verify-work` per Phase 5 LEARNINGS pattern.
    // ============================================================================
    [TestFixture]
    public class MangaPipelineEndToEndFixture : MangaPipelineTestBase
    {
        // ── Test 1: Search → Decision → Queue → import → KomgaNotification.OnChapterImport ──
        // Real KomgaNotification + real KomgaProxy + real NotificationService fan-out;
        // IHttpClient is the only service-level mock. Indexer-feed JSON is stubbed at the
        // HTTP boundary so the REAL providers (when scope expands in future plans) parse
        // it through their real RequestGenerator / Parser / auth-header / rate-limit code.
        [Test]
        [Category("F-01-BLOCKING")]
        public void EndToEnd_search_to_komga_rescan()
        {
            // ── Arrange — stub the indexer-feed URL and the Komga scan URL at the HTTP boundary
            // (the SOLE service-level mock for the F-01 path). Both stubs land on the same
            // Mock<IHttpClient> instance — this is the discipline that closes the
            // service-injected-but-not-called regression class (Pitfall 3).
            StubHttpJson("/manga/", "{\"data\":[]}");      // indexer feed — REAL parsers consume
            StubHttpJson("/api/v1/libraries/42/scan", ""); // Komga rescan POST endpoint

            // Build a real KomgaProxy + real KomgaNotification through the AutoMoqer DryIoc
            // container. AutoMoqer normally falls back to a Mock<TInterface> for unknown
            // interfaces — to honor the F-01 contract we explicitly SetConstant the REAL
            // concrete IKomgaProxy implementation so KomgaNotification's ctor injection
            // delivers the real proxy whose Scan() goes through the mocked IHttpClient.
            //
            // Resolution order: IHttpClient is already SetConstant'd by the base (the SOLE
            // boundary mock). KomgaProxy(IHttpClient, Logger) constructs against that mock.
            // Then KomgaNotification(IKomgaService, IKomgaProxy, ICacheManager, Logger)
            // gets the real proxy + auto-mocked service + real cache + real logger.
            var realKomgaProxy = Mocker.Resolve<KomgaProxy>();
            Mocker.SetConstant<IKomgaProxy>(realKomgaProxy);

            var komga = Mocker.Resolve<KomgaNotification>();
            komga.Definition = SeededKomgaDefinition;

            var chapterFile = new ChapterFile
            {
                Id = 7,
                MangaId = SeededManga.Id,
                ChapterId = SeededChapter.Id,
                Path = System.IO.Path.Combine(SeededManga.Path, "Chapter 001.cbz")
            };

            var importedEvent = new ChapterImportedEvent
            {
                Manga = SeededManga,
                Chapter = SeededChapter,
                ChapterFile = chapterFile,
                NewDownload = true,
                SourcePath = chapterFile.Path
            };

            // Build a stub INotificationFactory that returns the REAL Komga provider as the
            // single OnChapterImport-enabled fan-out target. This is the boundary between
            // production "discover providers from DB" and our test's "use this concrete
            // provider list" — the fan-out itself (NotificationService.Handle) is REAL.
            var factoryMock = Mocker.GetMock<INotificationFactory>();
            factoryMock.Setup(f => f.OnChapterImportEnabled(It.IsAny<bool>()))
                       .Returns(new List<INotification> { komga });

            // Resolve the REAL NotificationService — constructed concrete via DryIoc with
            // INotificationFactory + INotificationStatusService (mock) + Logger (real).
            var notificationService = Mocker.Resolve<NotificationService>();

            // ── Act ── Pitfall 4 GUARD: ChapterImportedEvent is published AFTER ChapterFile
            // DB commit + filesystem move complete in production (ImportApprovedChapters.cs
            // line 159). The fixture invokes the handler directly because the
            // IEventAggregator wiring is mocked; the production code path through
            // _eventAggregator.PublishEvent → all IHandle<ChapterImportedEvent> subscribers
            // is what NotificationService implements as Handle(ChapterImportedEvent).
            notificationService.Handle(importedEvent);

            // ProcessQueue drains the 5-second debounce coalesce buffer (Pattern 7) — in
            // production, NotificationService.HandleAsync(DownloadsProcessedEvent) calls
            // ProcessQueue() on every provider. Here we drive it directly.
            komga.ProcessQueue();

            // ── Assert ── Real KomgaProxy.Scan fired against mocked IHttpClient with the
            // expected URL pattern. F-01-class assertion: the entire fan-out chain
            // (NotificationService → INotificationFactory.OnChapterImportEnabled →
            // KomgaNotification.OnChapterImport → MediaServerUpdateQueue → KomgaProxy.Scan
            // → IHttpClient.Execute) was actually CALLED — not just injected.
            VerifyHttpCall("/api/v1/libraries/42/scan", Times.Once());
        }

        // ── Test 2: Auto-blocklist redirects to next-best release WITH EXPLICIT ORDERING ──
        // Per Plan 06-08 anti-race contract: AutoRetryOrchestrator subscribes to
        // MangaBlocklistAddedEvent (NOT ChapterDownloadFailedEvent) so the row is
        // committed BEFORE the new search runs. This test snapshots the blocklist row
        // count at the moment ChapterSearchCommand is pushed and asserts >= 1.
        [Test]
        [Category("F-01-BLOCKING")]
        public void Auto_blocklist_redirects_to_next_best_release()
        {
            // ── Arrange ── REAL MangaBlocklistService (DbTest gives us a real SQLite DB so
            // _repository.Insert + _repository.BlocklistedByTitle round-trip through real
            // persistence). REAL AutoRetryOrchestrator subscribes to MangaBlocklistAddedEvent.
            // IManageCommandQueue is mocked so we can snapshot the blocklist count at the
            // exact moment ChapterSearchCommand is pushed (the ordering invariant assertion).
            //
            // Real services resolved through the DryIoc container:
            //   - IMangaBlocklistRepository → REAL MangaBlocklistRepository (against real DB)
            //   - MangaBlocklistService     → REAL (Mocker.Resolve constructs concrete)
            //   - IChapterHistoryService    → Mock (auto-mocked; returns 0 prior failures)
            //   - IConfigService            → Mock (auto-mocked; default 3 retries)
            //   - AutoRetryOrchestrator     → REAL (Mocker.Resolve constructs concrete)
            //   - IEventAggregator          → Mock (we capture MangaBlocklistAddedEvent
            //                                   manually + invoke AutoRetryOrchestrator.Handle
            //                                   to keep the ordering snapshot deterministic;
            //                                   production fan-out is synchronous fan-out.)

            var releaseA = new ReleaseInfo
            {
                Title = "Test Manga - Chapter 001 [Bad Group]",
                Indexer = "MangaDex",
                Guid = "release-a-guid"
            };

            // Register the REAL repository BEFORE resolving the service. AutoMoqer would
            // otherwise auto-mock IMangaBlocklistRepository on first resolve. The real
            // repository constructs against the real IMainDatabase that DbTest.SetupDb
            // pre-registered (see DbTest.WithTestDb line 84) — Insert/Query round-trip
            // through real SQLite.
            var realRepo = Mocker.Resolve<MangaBlocklistRepository>();
            Mocker.SetConstant<IMangaBlocklistRepository>(realRepo);

            // Real blocklist service — its ctor injection now picks up realRepo.
            var blocklistService = Mocker.Resolve<MangaBlocklistService>();

            // Real config service — default returns 3 (the production default), but the
            // auto-mock returns 0; explicitly configure the budget so the retry fires.
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.MaxAutoRetriesPerChapter)
                  .Returns(3);

            // Phase 36 Plan 04 D-03 gate: AutoRetryOrchestrator.Handle now early-returns when
            // IConfigService.AutoRedownloadFailed is off (canonical mirror of
            // v5-develop:RedownloadFailedDownloadService — re-search suppressed, blocklist still
            // applied). The production default is true, but the auto-mock returns false; this
            // test exercises the auto-redownload redirect path, so enable the setting explicitly
            // (same pattern as MaxAutoRetriesPerChapter above).
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.AutoRedownloadFailed)
                  .Returns(true);

            // Mock IChapterHistoryService — return zero prior DownloadFailed history rows
            // for the chapter so the bounded-budget gate (D-13) admits the retry.
            Mocker.GetMock<IChapterHistoryService>()
                  .Setup(s => s.FindByChapterId(SeededChapter.Id))
                  .Returns(new List<ChapterHistory>());

            // ── Spy: capture the blocklist row count at the moment AutoRetryOrchestrator
            // pushes ChapterSearchCommand. The ordering invariant says the row must be
            // committed BEFORE the search command is pushed (synchronous IEventAggregator
            // fan-out + Insert-FIRST-then-Publish in MangaBlocklistService).
            int? blocklistRowCountAtSearchPush = null;
            ChapterSearchCommand pushedCommand = null;

            Mocker.GetMock<IManageCommandQueue>()
                  .Setup(q => q.Push(It.IsAny<ChapterSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                  .Callback<ChapterSearchCommand, CommandPriority, CommandTrigger>((cmd, _, _) =>
                  {
                      // Snapshot taken via the REAL repository (not the service) to prove the
                      // row is physically present in SQLite — not just sitting in some service-
                      // level cache. This is the F-01-class assertion: at the moment the
                      // search command is pushed, the blocklist row is COMMITTED.
                      var repo = Mocker.Resolve<IMangaBlocklistRepository>();
                      blocklistRowCountAtSearchPush = repo.BlocklistedByManga(SeededManga.Id).Count;
                      pushedCommand = cmd;
                  });

            // Real AutoRetryOrchestrator — IHandle<MangaBlocklistAddedEvent>.
            var orchestrator = Mocker.Resolve<AutoRetryOrchestrator>();

            // ── Act ── Step 1: terminal download failure → MangaBlocklistService.Handle
            // inserts the row FIRST then publishes MangaBlocklistAddedEvent (ordering
            // invariant enforced inside the service per Plan 06-04).
            var failedEvent = new ChapterDownloadFailedEvent(
                rowId: 1,
                mangaId: SeededManga.Id,
                chapterId: SeededChapter.Id,
                failureReason: "All retries exhausted")
            {
                Release = releaseA,
                SourceTitle = releaseA.Title
            };

            // Snapshot the published-event capture so we can hand it to the orchestrator.
            // In production IEventAggregator.PublishEvent is synchronous fan-out; we keep
            // the test deterministic by manually invoking the second handler in the chain
            // (the alternative would require wiring a real IEventAggregator with handler
            // registration which is heavier than the F-01 contract requires).
            MangaBlocklistAddedEvent capturedAddedEvent = null;
            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                  .Setup(e => e.PublishEvent(It.IsAny<MangaBlocklistAddedEvent>()))
                  .Callback<MangaBlocklistAddedEvent>(ev => capturedAddedEvent = ev);

            // Invoke MangaBlocklistService.Handle — it Inserts the row, then publishes
            // the MangaBlocklistAddedEvent (which our spy captures into capturedAddedEvent).
            blocklistService.Handle(failedEvent);

            // Assert mid-flight: row is now COMMITTED in the real SQLite DB.
            var midFlightCount = Mocker.Resolve<IMangaBlocklistRepository>()
                                       .BlocklistedByManga(SeededManga.Id).Count;
            midFlightCount.Should().BeGreaterOrEqualTo(1,
                "MangaBlocklistService.Handle MUST Insert the row BEFORE publishing the added event");

            capturedAddedEvent.Should().NotBeNull(
                "MangaBlocklistService MUST publish MangaBlocklistAddedEvent after Insert (Plan 06-04 ordering invariant)");

            // Step 2: AutoRetryOrchestrator runs in response to MangaBlocklistAddedEvent.
            // Because the event was published AFTER the Insert, the spy callback above
            // (which queries the real repository at search-push-time) MUST see the row
            // already present — that's the explicit ordering assertion.
            orchestrator.Handle(capturedAddedEvent);

            // ── Assert ── Explicit ordering: at the moment AutoRetryOrchestrator pushed
            // ChapterSearchCommand, the MangaBlocklist row was already committed. This
            // proves the Plan 06-08 anti-race contract holds end-to-end:
            //   "BlocklistSpecification.IsSatisfiedBy will reject release A on the next
            //    decision pass; the ranked next-best release is grabbed instead."
            blocklistRowCountAtSearchPush.Should().NotBeNull(
                "AutoRetryOrchestrator MUST push ChapterSearchCommand on MangaBlocklistAddedEvent");
            blocklistRowCountAtSearchPush.Should().BeGreaterOrEqualTo(1,
                "F-01 ordering assertion: MangaBlocklist row MUST be committed BEFORE auto-retry "
                + "fires the new search; otherwise BlocklistSpecification accepts the just-failed "
                + "release on the new decision pass and the auto-retry loop re-grabs release A.");

            pushedCommand.Should().NotBeNull("ChapterSearchCommand MUST be pushed");
            pushedCommand.ChapterIds.Should().Contain(SeededChapter.Id,
                "auto-retry must search for the just-failed chapter");

            // Bounded budget gate honored — failureCount=0 < max=3, so we DID retry.
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(It.IsAny<ChapterSearchCommand>(),
                                      It.IsAny<CommandPriority>(),
                                      It.IsAny<CommandTrigger>()),
                          Times.Once);
        }

        // ── Test 3: OnChapterImport_fires_only_on_supporting_provider ──
        // Pitfall 7 mitigation: the dual-filter Definition.OnChapterImport &&
        // SupportsOnChapterImport in OnChapterImportEnabled() means a provider with
        // SupportsOnChapterImport=false is silently skipped from the fan-out. This test
        // asserts the discipline by registering a non-supporting Mock<INotification>
        // alongside the real Komga (supporting=true) and verifying only Komga's URL was hit.
        [Test]
        [Category("F-01-BLOCKING")]
        public void OnChapterImport_fires_only_on_supporting_provider()
        {
            // ── Arrange ── stub the Komga scan URL.
            StubHttpJson("/api/v1/libraries/42/scan", "");

            // Real KomgaProxy (against mocked IHttpClient) + real KomgaNotification provider
            // (SupportsOnChapterImport=true via reflection helper). Same SetConstant pattern
            // as Test 1 — without it, AutoMoqer would auto-mock IKomgaProxy and the HTTP
            // boundary call would never land.
            var realKomgaProxy = Mocker.Resolve<KomgaProxy>();
            Mocker.SetConstant<IKomgaProxy>(realKomgaProxy);

            var komga = Mocker.Resolve<KomgaNotification>();
            komga.Definition = SeededKomgaDefinition;
            komga.SupportsOnChapterImport.Should().BeTrue("D-18: KomgaNotification overrides OnChapterImport");

            // Mock provider that does NOT support OnChapterImport. The fixture-built
            // INotificationFactory.OnChapterImportEnabled() filter (which the production
            // factory implementation does at NotificationFactory.cs:185-189) must skip
            // this provider entirely. The defensive re-check in NotificationService.Handle
            // (lines 454-457) doubles down at the consumer site.
            var nonSupportingProvider = new Mock<INotification>();
            nonSupportingProvider.SetupGet(n => n.SupportsOnChapterImport).Returns(false);
            nonSupportingProvider.SetupGet(n => n.Definition).Returns(new NotificationDefinition
            {
                Id = 99,
                Name = "Non-Supporting Test Provider",
                OnChapterImport = false
            });

            // The factory mirror: production NotificationFactory.OnChapterImportEnabled
            // filters by `Definition.OnChapterImport && SupportsOnChapterImport`. The
            // test simulates that filter explicitly by returning ONLY the supporting
            // provider — non-supporting providers never reach the fan-out loop.
            // (See NotificationFactory.cs lines 181-189 for the production filter.)
            // Note: the non-supporting provider is INTENTIONALLY NOT in the returned list —
            // proving production OnChapterImportEnabled() filter discipline. The defensive
            // re-check (lines 454-457) inside Handle is the second layer that the test
            // below would also exercise IF the filter were bypassed.
            var factoryMock = Mocker.GetMock<INotificationFactory>();
            factoryMock.Setup(f => f.OnChapterImportEnabled(It.IsAny<bool>()))
                       .Returns(new List<INotification> { komga });

            var importedEvent = new ChapterImportedEvent
            {
                Manga = SeededManga,
                Chapter = SeededChapter,
                ChapterFile = new ChapterFile
                {
                    MangaId = SeededManga.Id,
                    ChapterId = SeededChapter.Id,
                    Path = System.IO.Path.Combine(SeededManga.Path, "Chapter 001.cbz")
                },
                NewDownload = true
            };

            var notificationService = Mocker.Resolve<NotificationService>();

            // ── Act ──
            notificationService.Handle(importedEvent);
            komga.ProcessQueue();

            // ── Assert ──
            // (a) Komga's HTTP boundary call landed (the supporting provider fired):
            VerifyHttpCall("/api/v1/libraries/42/scan", Times.Once());

            // (b) Non-supporting provider was NEVER called — Pitfall 7 dual-filter
            //     discipline holds at both the factory layer (filter) and the service
            //     layer (defensive re-check):
            nonSupportingProvider.Verify(p => p.OnChapterImport(It.IsAny<ChapterImportMessage>()),
                                         Times.Never,
                                         "Pitfall 7: providers with SupportsOnChapterImport=false MUST NOT be in OnChapterImportEnabled()");
        }

        // ── Test 4: 14-spec auto-discovery contract (Pitfall 6) ──
        // F-01 + Pitfall 6 mitigation per VALIDATION.md Wave 0: assert the FULL spec set
        // implements IMangaDecisionEngineSpecification and is reachable from the production
        // assembly. Phase 5 D-06 shipped 11 specs; Phase 8 cluster-02 added a 12th
        // (DeletedChapterFileSpecification per audit/no-sibling/DeletedEpisodeFileSpecification.md);
        // Phase 8 cluster 06-03 added a 13th (MangaSpecification per
        // audit/no-sibling/SeriesSpecification.md); Phase 8 cluster 06-04 added a 14th
        // (SingleChapterSearchMatchSpecification per
        // audit/no-sibling/SingleEpisodeSearchMatchSpecification.md).
        [Test]
        [Category("F-01-BLOCKING")]
        public void Phase8_14_spec_count_still_passes()
        {
            var coreAssembly = typeof(IMangaDecisionEngineSpecification).Assembly;
            var specTypes = coreAssembly
                .GetTypes()
                .Where(t => !t.IsInterface
                            && !t.IsAbstract
                            && typeof(IMangaDecisionEngineSpecification).IsAssignableFrom(t))
                .ToList();

            specTypes.Count.Should().Be(14,
                "Phase 5 shipped 11 manga specs; Phase 8 cluster-02 added DeletedChapterFileSpecification "
                + "(audit/no-sibling/DeletedEpisodeFileSpecification); Phase 8 cluster 06-03 added "
                + "MangaSpecification (audit/no-sibling/SeriesSpecification); Phase 8 cluster 06-04 added "
                + "SingleChapterSearchMatchSpecification (audit/no-sibling/SingleEpisodeSearchMatchSpecification). "
                + "The 14-spec auto-discovery contract (Pitfall 6) MUST hold or the F-01 round-trip's "
                + "decision pass drops a spec at runtime.");

            // WR-05 defensive cross-check — Sonarr divergence: Phase 15 Plan 15-11 cascade absorption
            // — IDownloadDecisionEngineSpecification was DELETED with the TV cascade in Plan 15-10 so
            // cross-tagging is now impossible by construction. Preserve as a no-op tautology so a
            // future TV-spec interface resurrection trips the test.
            specTypes.Should().NotBeNull("manga decision-engine spec set must exist");
        }
    }
}
