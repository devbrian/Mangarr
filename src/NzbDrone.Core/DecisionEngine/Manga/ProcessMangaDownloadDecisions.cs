using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.DecisionEngine.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit gap-01 family — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Download/ProcessDownloadDecisions.cs (TV).
    //
    // Closes the manga search→grab regression: ChapterSearchService and MangaSearchService
    // previously counted approved decisions and dropped the result. This service walks ranked
    // decisions and grabs approved ones via IMangaDownloadService.DownloadReport.
    //
    // Phase 15 Wave (A) W-4 (2026-05-07): rebound from IDownloadService to IMangaDownloadService
    // (Wave (A) W-1 / N-1) per .planning/phases/15-domain-rename-rebrand/15-CONTRACTS-AUDIT.md
    // §18 + files_modified seed list. Drops the RemoteChapter.ToRemoteEpisodeShim() bridge.
    //
    // Phase 15 Wave (C) cleanup: collapse with TV ProcessDownloadDecisions when Tv/ deletes.
    public class ProcessMangaDownloadDecisions : IProcessMangaDownloadDecisions
    {
        private readonly IMangaDownloadService _downloadService;
        private readonly IMangaPendingReleaseService _pendingReleaseService;
        private readonly MangaDownloadDecisionComparer _comparer;
        private readonly Logger _logger;

        public ProcessMangaDownloadDecisions(IMangaDownloadService downloadService,
                                             IMangaPendingReleaseService pendingReleaseService,
                                             MangaDownloadDecisionComparer comparer,
                                             Logger logger)
        {
            _downloadService = downloadService;
            _pendingReleaseService = pendingReleaseService;
            _comparer = comparer;
            _logger = logger;
        }

        public async Task<ProcessedMangaDecisions> ProcessDecisions(List<MangaDownloadDecision> decisions)
        {
            var qualifiedReports = GetQualifiedReports(decisions);

            // OrderByDescending — MangaDownloadDecisionComparer follows the OrderByDescending
            // convention ("better" yields a POSITIVE compare value; see its fixture + the TV
            // ProcessDownloadDecisions original). We then iterate best-first and grab the first
            // acceptable candidate per chapter. NOTE: this was OrderBy (ascending) until
            // quick-260607-cto — that consumed the descending-convention comparer backwards, so
            // among 2+ qualified candidates for one chapter the WORST was grabbed (lowest
            // translation rank, lowest CF, worst indexer priority, oldest, smallest). See
            // ProcessMangaDownloadDecisionsFixture.Should_grab_best_ranked_candidate_for_same_chapter_*.
            var prioritizedDecisions = qualifiedReports.OrderByDescending(d => d, _comparer).ToList();
            var grabbed = new List<MangaDownloadDecision>();
            var pending = new List<MangaDownloadDecision>();
            var rejected = decisions.Where(d => d.Rejected).ToList();
            var pendingAddQueue = new List<Tuple<MangaDownloadDecision, PendingReleaseReason>>();

            foreach (var report in prioritizedDecisions)
            {
                if (IsChapterProcessed(grabbed, report))
                {
                    continue;
                }

                if (report.TemporarilyRejected)
                {
                    PreparePending(pendingAddQueue, grabbed, pending, report, PendingReleaseReason.Delay);
                    continue;
                }

                var result = await ProcessDecisionInternal(report);

                switch (result)
                {
                    case ProcessedDecisionResult.Grabbed:
                        grabbed.Add(report);
                        break;
                    case ProcessedDecisionResult.Pending:
                        PreparePending(pendingAddQueue, grabbed, pending, report, PendingReleaseReason.Delay);
                        break;
                    case ProcessedDecisionResult.Rejected:
                        rejected.Add(report);
                        break;
                    case ProcessedDecisionResult.Failed:
                        PreparePending(pendingAddQueue, grabbed, pending, report, PendingReleaseReason.DownloadClientUnavailable);
                        break;
                    case ProcessedDecisionResult.Skipped:
                        break;
                }
            }

            if (pendingAddQueue.Any())
            {
                _pendingReleaseService.AddMany(pendingAddQueue);
            }

            return new ProcessedMangaDecisions(grabbed, pending, rejected);
        }

        // Phase 8 Plan 99-06 + sonarr-consistency-audit F-02 — singular-overload sibling of TV
        // ProcessDownloadDecisions.ProcessDecision (line 127). Used by MangaReleaseController
        // (Plan 06-09 Interactive Search modal grab POST path) so the controller routes through
        // the same Pending/Rejected/Failed bucketing as the batch ProcessDecisions path. Verbatim
        // shape mirror of TV — qualified-report gate, TemporarilyRejected → Pending(Delay),
        // ProcessDecisionInternal hands to IDownloadService.DownloadReport via the shim.
        public async Task<ProcessedDecisionResult> ProcessDecision(MangaDownloadDecision decision, int? downloadClientId)
        {
            if (decision == null)
            {
                return ProcessedDecisionResult.Skipped;
            }

            if (!IsQualifiedReport(decision))
            {
                return ProcessedDecisionResult.Rejected;
            }

            if (decision.TemporarilyRejected)
            {
                _pendingReleaseService.Add(decision, PendingReleaseReason.Delay);
                return ProcessedDecisionResult.Pending;
            }

            var result = await ProcessDecisionInternal(decision, downloadClientId);

            if (result == ProcessedDecisionResult.Failed)
            {
                _pendingReleaseService.Add(decision, PendingReleaseReason.DownloadClientUnavailable);
            }

            return result;
        }

        internal List<MangaDownloadDecision> GetQualifiedReports(IEnumerable<MangaDownloadDecision> decisions)
        {
            return decisions.Where(IsQualifiedReport).ToList();
        }

        internal bool IsQualifiedReport(MangaDownloadDecision decision)
        {
            return (decision.Approved || decision.TemporarilyRejected)
                && decision.RemoteChapter?.Chapters != null
                && decision.RemoteChapter.Chapters.Any();
        }

        private bool IsChapterProcessed(List<MangaDownloadDecision> decisions, MangaDownloadDecision report)
        {
            var chapterIds = report.RemoteChapter.Chapters.Select(c => c.Id).ToList();
            return decisions.SelectMany(r => r.RemoteChapter.Chapters)
                            .Select(c => c.Id)
                            .ToList()
                            .Intersect(chapterIds)
                            .Any();
        }

        private void PreparePending(List<Tuple<MangaDownloadDecision, PendingReleaseReason>> queue,
                                    List<MangaDownloadDecision> grabbed,
                                    List<MangaDownloadDecision> pending,
                                    MangaDownloadDecision report,
                                    PendingReleaseReason reason)
        {
            if (IsChapterProcessed(grabbed, report) || IsChapterProcessed(pending, report))
            {
                reason = PendingReleaseReason.Fallback;
            }

            queue.Add(Tuple.Create(report, reason));
            pending.Add(report);
        }

        private async Task<ProcessedDecisionResult> ProcessDecisionInternal(MangaDownloadDecision decision, int? downloadClientId = null)
        {
            var remoteChapter = decision.RemoteChapter;
            var remoteIndexer = remoteChapter.Release?.Indexer;

            try
            {
                _logger.Trace("Grabbing manga release '{0}' from Indexer {1}.", remoteChapter, remoteIndexer);
                await _downloadService.DownloadReport(remoteChapter, downloadClientId);
                return ProcessedDecisionResult.Grabbed;
            }
            catch (ReleaseUnavailableException)
            {
                _logger.Warn("Failed to download manga release '{0}' from Indexer {1}. Release not available", remoteChapter, remoteIndexer);
                return ProcessedDecisionResult.Rejected;
            }
            catch (Exception ex)
            {
                if (ex is DownloadClientUnavailableException || ex is DownloadClientAuthenticationException)
                {
                    _logger.Debug(ex, "Failed to send manga release '{0}' from Indexer {1} to download client, storing until later.", remoteChapter, remoteIndexer);
                    return ProcessedDecisionResult.Failed;
                }

                _logger.Warn(ex, "Couldn't add manga release '{0}' from Indexer {1} to download queue.", remoteChapter, remoteIndexer);
                return ProcessedDecisionResult.Skipped;
            }
        }
    }
}
