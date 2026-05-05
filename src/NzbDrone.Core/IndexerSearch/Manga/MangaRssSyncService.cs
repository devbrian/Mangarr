using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-07 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Indexers/FetchAndParseRssService.cs (lines 14-62).
    //
    // D-07 fan-out: filters _indexerFactory.RssEnabled() to Protocol == DownloadProtocol.Http
    // and honors per-IndexerDefinition.SyncInterval override (default 0 = use global
    // IConfigService.MangaRssSyncInterval). LastRssSync timestamps the most recent
    // successful fetch; never-synced indexers are treated as due. Decisions land in
    // Phase 5 IMakeMangaDownloadDecision.GetRssDecision; Plan 06-07/08 own the grab path.
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
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MangaRssSyncService(IIndexerFactory indexerFactory,
                                   IMakeMangaDownloadDecision decisionMaker,
                                   IConfigService configService,
                                   IEventAggregator eventAggregator,
                                   Logger logger)
        {
            _indexerFactory = indexerFactory;
            _decisionMaker = decisionMaker;
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

            _logger.Debug("Polling {0} manga indexer(s) for RSS feeds", indexers.Count);

            var tasks = indexers.Select(FetchIndexerSafe);
            var batch = Task.WhenAll(tasks).GetAwaiter().GetResult();
            var reports = batch.SelectMany(x => x).ToList();

            _logger.Debug("Found {0} manga reports across {1} indexer(s)", reports.Count, indexers.Count);

            // Decisions surface for the Plan 06-07/08 grab/import consumers.
            // No process-decisions wiring here — Phase 5 produces MangaDownloadDecision
            // (manga-shape) which the TV IProcessDownloadDecisions cannot consume.
            //
            // Plan 09-12 — capture decisions for the manga-shape MangaRssSyncCompleteEvent payload.
            // Pitfall 4 ordering: this is the BATCH event publish — LastRssSync per-indexer DB writes
            // have already completed (inside FetchIndexerSafe per success path). Plan 09-10
            // MangaPendingReleaseService.Handle(MangaRssSyncCompleteEvent) prunes rejected pending releases.
            var decisions = _decisionMaker.GetRssDecision(reports);
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
