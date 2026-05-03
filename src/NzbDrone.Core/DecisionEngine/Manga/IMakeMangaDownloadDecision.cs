using System.Collections.Generic;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Manga
{
    // Sonarr divergence: NEW manga-side decision orchestrator interface per Phase 5 D-05 — see DIVERGENCE.md.
    // Phase 6 search/RSS dispatchers will call these methods. Mirrors IMakeDownloadDecision shape
    // (DownloadDecisionMaker.cs:18-22) with type swap: DownloadDecision -> MangaDownloadDecision and
    // SearchCriteriaBase -> MangaSearchCriteriaBase (Phase 5 D-05 manga-shape divergence).
    public interface IMakeMangaDownloadDecision
    {
        List<MangaDownloadDecision> GetRssDecision(List<ReleaseInfo> reports, bool pushedRelease = false);
        List<MangaDownloadDecision> GetSearchDecision(List<ReleaseInfo> reports, MangaSearchCriteriaBase searchCriteria);
    }
}
