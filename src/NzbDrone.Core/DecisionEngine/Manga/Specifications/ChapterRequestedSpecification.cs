using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Role-match analog: MonitoredEpisodeSpecification search-criteria branch at
    // src/NzbDrone.Core/DecisionEngine/Specifications/RssSync/MonitoredEpisodeSpecification.cs.
    // During interactive search, the user explicitly requested a set of chapters; this spec
    // rejects releases that do not include any of the requested chapters. Reads the canonical
    // Phase 3 shape MangaSearchCriteriaBase.Chapters : List<Chapter> directly (no reflection).
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class ChapterRequestedSpecification : IMangaDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public ChapterRequestedSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            var criteria = information?.MangaSearchCriteria;
            if (criteria == null)
            {
                // Not a search — not our concern (RSS path).
                return DownloadSpecDecision.Accept();
            }

            if (criteria.Chapters == null || !criteria.Chapters.Any())
            {
                // Whole-manga search with no narrowed chapter list — accept (caller filters).
                return DownloadSpecDecision.Accept();
            }

            var requestedIds = new HashSet<int>(criteria.Chapters.Select(c => c.Id));
            if (subject.Chapters == null || !subject.Chapters.Any(c => requestedIds.Contains(c.Id)))
            {
                _logger.Debug("Release does not contain any requested chapter");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.ChapterNotRequested, "No requested chapter in release");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
