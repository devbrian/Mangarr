using NLog;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Mirrors MonitoredEpisodeSpecification first-half (is-series-monitored check) at
    // src/NzbDrone.Core/DecisionEngine/Specifications/RssSync/MonitoredEpisodeSpecification.cs.
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class MonitoredMangaSpecification : IMangaDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public MonitoredMangaSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            if (information?.MangaSearchCriteria != null)
            {
                _logger.Debug("Skipping monitored manga check during search");
                return DownloadSpecDecision.Accept();
            }

            if (subject.Manga == null || !subject.Manga.Monitored)
            {
                _logger.Debug("{0} is not monitored", subject.Manga);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.MangaNotMonitored, "Manga is not monitored");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
