using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-07 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Indexers/FetchAndParseRssService.cs (lines 14-62)
    // + src/NzbDrone.Core/Indexers/RssSyncService.cs (TV outer pipeline orchestrator).
    //
    // D-07 fan-out: filters _indexerFactory.RssEnabled() to Protocol == DownloadProtocol.Http
    // and honors per-IndexerDefinition.SyncInterval override (default 0 = use global
    // IConfigService.MangaRssSyncInterval). LastRssSync timestamps the most recent
    // successful fetch; never-synced indexers are treated as due.
    //
    // Debug-session `queue-items-not-downloading` (2026-05-13) close-out: backfills the
    // canonical TV RssSyncService.Sync() pipeline that was deferred in Phase 9 audit gap
    // `RssSyncService-vs-MangaRssSyncService.md`. Two slices land here:
    //
    //   1. CONCAT-PENDING — _pendingReleaseService.GetPending() is concatenated into the
    //      reports list BEFORE the decision-maker runs. Pending releases (e.g. queued
    //      with PendingReleaseReason.DownloadClientUnavailable) get re-evaluated each
    //      tick. Without this, pending rows sit in MangaPendingReleases forever even
    //      when the underlying blocker (e.g. download client offline) has cleared.
    //
    //   2. GRAB-DECISIONS — _processDownloadDecisions.ProcessDecisions(decisions) hands
    //      approved decisions to IMangaDownloadService.DownloadReport. Rejected /
    //      pending / failed are bucketed via the canonical Pending-or-Grab pipeline
    //      (PendingReleaseReason.Delay, .DownloadClientUnavailable, .Fallback). Without
    //      this, RSS-fetched releases produced decisions that were thrown away — the
    //      RSS sync found releases but never grabbed them.
    //
    // The MangaRssSyncCompleteEvent payload remains the pre-process decision list so
    // MangaPendingReleaseService.Handle(MangaRssSyncCompleteEvent) can still filter
    // `.Rejected` to prune the pending table (the rejected-side mutation trigger).
    //
    // Phase 8 cleanup: collapse with FetchAndParseRssService + RssSyncService when Tv/ deletes.
    //
    // Phase 9 Plan 09-12 (sub-wave A 09-02 audit gap-01 + gap-02 close-out): publishes
    // MangaRssSyncCompleteEvent at end of Execute (Plan 09-10 IHandle wiring); persists
    // LastRssSync = DateTime.UtcNow per indexer on successful FetchRecent (D-07 per-source
    // rate budget feature lights up). Pitfall 4 ordering preserved: per-decision DB write
    // FIRST (LastRssSync inside FetchIndexerSafe), batch event LAST (single PublishEvent
    // at the tail of Execute).
    public class MangaRssSyncService : IExecute<MangaRssSyncCommand>
    {
        private readonly IIndexerFactory _indexerFactory;
        private readonly IMakeMangaDownloadDecision _decisionMaker;
        private readonly IProcessMangaDownloadDecisions _processDownloadDecisions;
        private readonly IMangaPendingReleaseService _pendingReleaseService;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MangaRssSyncService(IIndexerFactory indexerFactory,
                                   IMakeMangaDownloadDecision decisionMaker,
                                   IProcessMangaDownloadDecisions processDownloadDecisions,
                                   IMangaPendingReleaseService pendingReleaseService,
                                   IConfigService configService,
                                   IEventAggregator eventAggregator,
                                   Logger logger)
        {
            _indexerFactory = indexerFactory;
            _decisionMaker = decisionMaker;
            _processDownloadDecisions = processDownloadDecisions;
            _pendingReleaseService = pendingReleaseService;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void Execute(MangaRssSyncCommand message)
        {
            var globalInterval = TimeSpan.FromMinutes(_configService.MangaRssSyncInterval);

            var indexers = _indexerFactory.RssEnabled()
                .Where(i => i.Protocol == DownloadProtocol.Http)
                .Where(i => DueForRefresh(i, globalInterval))
                .ToList();

            if (!indexers.Any())
            {
                _logger.Debug("No manga RSS-enabled indexers due for refresh");
                return;
            }

            _logger.ProgressInfo("Starting Manga RSS Sync");

            var tasks = indexers.Select(FetchIndexerSafe);
            var batch = Task.WhenAll(tasks).GetAwaiter().GetResult();
            var rssReleases = batch.SelectMany(x => x).ToList();

            // Debug-session queue-items-not-downloading (2026-05-13) — mirror TV
            // RssSyncService.Sync() lines 41-43: concat pending releases into the reports
            // pool BEFORE decision-making so the decision-maker re-evaluates the pending
            // queue every tick. Without this, pending releases sit in
            // MangaPendingReleases forever even when their original blocker clears.
            var pendingReleases = _pendingReleaseService.GetPending();
            var reports = rssReleases.Concat(pendingReleases).ToList();

            _logger.Debug("Found {0} manga reports across {1} indexer(s) ({2} fresh + {3} pending)",
                reports.Count,
                indexers.Count,
                rssReleases.Count,
                pendingReleases.Count);

            var decisions = _decisionMaker.GetRssDecision(reports);

            // Debug-session queue-items-not-downloading (2026-05-13) — mirror TV
            // RssSyncService.Sync() line 44: hand approved decisions to the grab pipeline.
            // ProcessMangaDownloadDecisions buckets results into Grabbed / Pending / Rejected
            // via IMangaDownloadService.DownloadReport (Grabbed) and IMangaPendingReleaseService.AddMany
            // (Pending). Previously the decision list was discarded — RSS found releases
            // but never grabbed them.
            var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

            if (processed.Pending.Any())
            {
                _logger.ProgressInfo(
                    "Manga RSS Sync Completed. Reports found: {0}, Reports grabbed: {1}, Reports pending: {2}",
                    reports.Count,
                    processed.Grabbed.Count,
                    processed.Pending.Count);
            }
            else
            {
                _logger.ProgressInfo(
                    "Manga RSS Sync Completed. Reports found: {0}, Reports grabbed: {1}",
                    reports.Count,
                    processed.Grabbed.Count);
            }

            // Plan 09-12 — pre-process decision list is the event payload so
            // MangaPendingReleaseService.Handle(MangaRssSyncCompleteEvent) can filter
            // `.Rejected` to prune stale pending rows. Pitfall 4 ordering: per-indexer
            // LastRssSync writes happened inside FetchIndexerSafe (success path); grab
            // writes happened inside ProcessDecisions; the batch event publish is the
            // LAST line of Execute.
            _eventAggregator.PublishEvent(new MangaRssSyncCompleteEvent(decisions));
        }

        private async Task<IList<ReleaseInfo>> FetchIndexerSafe(IIndexer indexer)
        {
            try
            {
                var reports = await indexer.FetchRecent();

                // Plan 09-12 (audit gap-02) — persist LastRssSync on successful fetch ONLY.
                // The per-indexer SyncInterval override (Phase 6 D-07) reads this in
                // DueForRefresh below; without this write, def.LastRssSync == null is permanently
                // true and the override is unreachable in practice. Pitfall 4 ordering: DB write
                // FIRST, batch event publish LAST (the batch event is published in Execute after
                // all FetchIndexerSafe calls complete via Task.WhenAll).
                // MUST happen INSIDE try AFTER await — a thrown FetchRecent must not leave a
                // stale timestamp on disk.
                if (indexer.Definition is IndexerDefinition def)
                {
                    def.LastRssSync = DateTime.UtcNow;
                    _indexerFactory.Update(def);
                }

                return reports;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during manga RSS sync on {0}", indexer.Definition.Name);
                return Array.Empty<ReleaseInfo>();
            }
        }

        // D-07: per-IndexerDefinition.SyncInterval > 0 overrides the global default.
        // SyncInterval == 0 means "use global". LastRssSync == null means "never synced — due now".
        // indexer.Definition surfaces ProviderDefinition (the abstract base) — cast to
        // IndexerDefinition where Plan 06-01 added the SyncInterval / LastRssSync columns.
        private bool DueForRefresh(IIndexer indexer, TimeSpan globalInterval)
        {
            if (indexer.Definition is not IndexerDefinition def)
            {
                return true;
            }

            var perIndexerOverride = def.SyncInterval > 0
                ? TimeSpan.FromMinutes(def.SyncInterval)
                : (TimeSpan?)null;
            var effective = perIndexerOverride ?? globalInterval;

            if (def.LastRssSync == null)
            {
                return true;
            }

            return (DateTime.UtcNow - def.LastRssSync.Value) >= effective;
        }
    }
}
