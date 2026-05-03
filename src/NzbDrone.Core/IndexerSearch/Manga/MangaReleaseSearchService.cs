using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-06/D-08 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/IndexerSearch/ReleaseSearchService.cs (lines 28-574).
    //
    // D-08 fan-out: filters _indexerFactory.AutomaticSearchEnabled() to
    // Protocol == DownloadProtocol.Http (manga indexers from Phase 3), parallel-fetches
    // Fetch(MangaSearchCriteria) / Fetch(ChapterSearchCriteria), per-indexer try/catch
    // isolation (one bad indexer doesn't kill the batch — same precedent as
    // FetchAndParseRssService.FetchIndexer lines 49-61), then runs Phase 5
    // MangaDownloadDecisionMaker.GetSearchDecision against the aggregated reports.
    //
    // Indexer surface choice: AutomaticSearchEnabled (NOT InteractiveSearchEnabled)
    // because Plan 06-06 dispatches scheduled / wanted-sweep / on-add searches —
    // the interactive (modal) flavor lands in Plan 06-09 V5 controller wiring.
    //
    // Phase 8 cleanup: collapse with ReleaseSearchService when Tv/ deletes.
    public class MangaReleaseSearchService : IMangaSearchForReleases
    {
        private readonly IIndexerFactory _indexerFactory;
        private readonly IMakeMangaDownloadDecision _decisionMaker;
        private readonly Logger _logger;

        public MangaReleaseSearchService(IIndexerFactory indexerFactory,
                                         IMakeMangaDownloadDecision decisionMaker,
                                         Logger logger)
        {
            _indexerFactory = indexerFactory;
            _decisionMaker = decisionMaker;
            _logger = logger;
        }

        public async Task<List<MangaDownloadDecision>> MangaSearch(MangaSearchCriteria criteria)
        {
            var reports = await FetchFromIndexers(indexer => indexer.Fetch(criteria), "manga");
            return _decisionMaker.GetSearchDecision(reports, criteria).ToList();
        }

        public async Task<List<MangaDownloadDecision>> ChapterSearch(ChapterSearchCriteria criteria)
        {
            var reports = await FetchFromIndexers(indexer => indexer.Fetch(criteria), "chapter");
            return _decisionMaker.GetSearchDecision(reports, criteria).ToList();
        }

        private async Task<List<ReleaseInfo>> FetchFromIndexers(Func<IIndexer, Task<IList<ReleaseInfo>>> fetch, string searchKind)
        {
            var indexers = _indexerFactory.AutomaticSearchEnabled()
                .Where(i => i.Protocol == DownloadProtocol.Http)
                .ToList();

            if (!indexers.Any())
            {
                _logger.Warn("No manga indexers available, {0} search aborted", searchKind);
                return new List<ReleaseInfo>();
            }

            _logger.Debug("Available manga indexers: {0}", indexers.Count);

            var tasks = indexers.Select(async indexer =>
            {
                try
                {
                    return await fetch(indexer);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during manga {0} search on {1}", searchKind, indexer.Definition.Name);
                    return (IList<ReleaseInfo>)Array.Empty<ReleaseInfo>();
                }
            });

            var batch = await Task.WhenAll(tasks);
            return batch.SelectMany(x => x).ToList();
        }
    }
}
