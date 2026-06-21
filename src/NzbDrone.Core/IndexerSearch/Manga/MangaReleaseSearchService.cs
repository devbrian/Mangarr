using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Manga;
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
        private readonly IChapterSynthesisService _chapterSynthesisService;
        private readonly IChapterService _chapterService;
        private readonly Logger _logger;

        public MangaReleaseSearchService(IIndexerFactory indexerFactory,
                                         IMakeMangaDownloadDecision decisionMaker,
                                         IChapterSynthesisService chapterSynthesisService,
                                         IChapterService chapterService,
                                         Logger logger)
        {
            _indexerFactory = indexerFactory;
            _decisionMaker = decisionMaker;
            _chapterSynthesisService = chapterSynthesisService;
            _chapterService = chapterService;
            _logger = logger;
        }

        public async Task<List<MangaDownloadDecision>> MangaSearch(MangaSearchCriteria criteria)
        {
            var reports = await FetchFromIndexers(indexer => indexer.Fetch(criteria), "manga");
            var decisions = _decisionMaker.GetSearchDecision(reports, criteria).ToList();

            // Phase 40 RECON-02 / D-01: whole-manga [1..maxWhole] (plus Chapter 0 only when a
            // chapter-0 release is attributed) catalog backfill. The
            // external gateway exposes chapter releases the MangaDex metadata catalog never
            // enumerated; mirror the genuinely-missing WHOLE numbers into the local Chapter
            // catalog so RSS/missing search can discover them. Synthesis is a pure side-effect
            // (attribution-gated, idempotent via SyncChapters) — the decision list is returned
            // unchanged. MangaSearch ONLY (NOT ChapterSearch — RESEARCH Open Question #1: the
            // chapter-scoped path relies on the on-grab hook in MangaReleaseController).
            //
            // Best-effort (CodeRabbit PR #328): synthesis is a side-effect — a throw must NOT
            // abort the search or swallow the decisions the user/RSS-sync is waiting for.
            var synthesizedCount = 0;
            try
            {
                synthesizedCount = _chapterSynthesisService.SynthesizeFromDecisions(criteria.Manga, decisions);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex,
                    "Chapter synthesis failed for manga search '{0}' (id={1}); returning decisions unchanged.",
                    criteria.Manga?.Title,
                    criteria.Manga?.Id);
            }

            // debug `per-manga-search-2-runs` (2026-06-21): SAME-TICK RE-DECISION so the chapters
            // synthesis just created GRAB in THIS search instead of requiring a second per-manga
            // search. Mirrors MangaRssSyncService's same-tick re-grab (Phase 40 RSS self-heal).
            //
            // The first GetSearchDecision above ran BEFORE the new Chapter rows existed, so a
            // release for a brand-new chapter mapped to an EMPTY RemoteChapter.Chapters
            // (MangaParsingService.Map's DB fallback found nothing) and was rejected by
            // ChapterRequestedSpecification (not in criteria.Chapters) / MonitoredChapterSpecification
            // (empty chapters). Synthesis then created those rows but the returned decision list
            // still reflected the pre-synthesis catalog — so the user had to search a 2nd time.
            //
            // Refresh criteria.Chapters to the now-current set and re-run the decision pass against
            // the SAME reports. The refresh is keyed off the same MonitoredChaptersOnly flag each
            // caller set when it built criteria.Chapters, so it reproduces EXACTLY what a manual 2nd
            // search would compute: the automatic search (MangaSearchService, MonitoredChaptersOnly=true)
            // re-derives Monitored && no-file; the interactive controller (MangaReleaseController,
            // MonitoredChaptersOnly=false) re-derives the full chapter list. Map's DB fallback then
            // resolves the freshly-synthesized rows so they qualify and flow to the grab pipeline.
            //
            // Count-gated: steady state (catalog already complete -> synthesizedCount == 0) pays for
            // exactly one decision pass. Synthesis only ADDS monitored rows (SyncChapters never
            // deletes / never flips Monitored on update), so a release approved on the first pass can
            // never be downgraded by the second.
            if (synthesizedCount > 0 && criteria.Manga != null)
            {
                var allChapters = _chapterService.GetChaptersByManga(criteria.Manga.Id)
                                  ?? new List<Chapter>();

                criteria.Chapters = criteria.MonitoredChaptersOnly
                    ? allChapters.Where(c => c.Monitored && c.ChapterFileId == null).ToList()
                    : allChapters.ToList();

                _logger.Debug(
                    "Synthesized {0} new chapter row(s) during manga search '{1}' (id={2}); re-evaluating reports against the updated catalog.",
                    synthesizedCount,
                    criteria.Manga.Title,
                    criteria.Manga.Id);

                decisions = _decisionMaker.GetSearchDecision(reports, criteria).ToList();
            }

            return decisions;
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
