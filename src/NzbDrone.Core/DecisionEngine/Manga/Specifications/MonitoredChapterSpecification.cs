using System.Linq;
using NLog;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Mirrors MonitoredEpisodeSpecification chapter-iteration half at
    // src/NzbDrone.Core/DecisionEngine/Specifications/RssSync/MonitoredEpisodeSpecification.cs:30-57.
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class MonitoredChapterSpecification : IMangaDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public MonitoredChapterSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            // Honor MonitoredChaptersOnly toggle on the manga search criteria. When MonitoredChaptersOnly=false
            // (interactive search default), skip the per-chapter monitored check entirely — mirrors TV analog
            // at MonitoredEpisodeSpecification.cs:21 which short-circuits on { MonitoredEpisodesOnly: false }.
            if (information?.MangaSearchCriteria is { MonitoredChaptersOnly: false })
            {
                _logger.Debug("Skipping monitored chapter check during search (MonitoredChaptersOnly=false)");
                return DownloadSpecDecision.Accept();
            }

            if (subject.Chapters == null || !subject.Chapters.Any())
            {
                _logger.Debug("Release contains no chapters");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterMissingFromRelease, "No chapters in release");
            }

            var monitoredCount = subject.Chapters.Count(c => c.Monitored);
            if (monitoredCount == subject.Chapters.Count)
            {
                return DownloadSpecDecision.Accept();
            }

            if (monitoredCount == 0)
            {
                _logger.Debug("No chapters in the release are monitored");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterNotMonitored, "Chapter(s) not monitored");
            }

            _logger.Debug("Only {0}/{1} chapters in the release are monitored. Rejecting", monitoredCount, subject.Chapters.Count);
            return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterNotMonitored, "One or more chapters is not monitored");
        }
    }
}
