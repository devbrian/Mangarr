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
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit gap-01 family — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Download/ProcessDownloadDecisions.cs (TV).
    //
    // Closes the manga search→grab regression: ChapterSearchService and MangaSearchService
    // previously counted approved decisions and dropped the result. This service walks ranked
    // decisions and grabs approved ones via IDownloadService.DownloadReport (using the
    // RemoteChapter.ToRemoteEpisodeShim() bridge until the Phase 15 unified IDownloadService
    // lands).
    //
    // Phase 15 cleanup: collapse with TV ProcessDownloadDecisions when Tv/ deletes; the shim
    // disappears alongside.
    public class ProcessMangaDownloadDecisions : IProcessMangaDownloadDecisions
    {
        private readonly IDownloadService _downloadService;
        private readonly IMangaPendingReleaseService _pendingReleaseService;
        private readonly MangaDownloadDecisionComparer _comparer;
        private readonly Logger _logger;

        public ProcessMangaDownloadDecisions(IDownloadService downloadService,
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
            var prioritizedDecisions = qualifiedReports.OrderBy(d => d, _comparer).ToList();
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

        private async Task<ProcessedDecisionResult> ProcessDecisionInternal(MangaDownloadDecision decision)
        {
            var remoteChapter = decision.RemoteChapter;
            var remoteIndexer = remoteChapter.Release?.Indexer;

            try
            {
                _logger.Trace("Grabbing manga release '{0}' from Indexer {1}.", remoteChapter, remoteIndexer);
                await _downloadService.DownloadReport(remoteChapter.ToRemoteEpisodeShim(), downloadClientId: null);
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
