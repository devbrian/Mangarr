using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-06/D-08 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/IndexerSearch/ReleaseSearchService.cs ISearchForReleases.
    //
    // Manga-shaped fan-out: parallel-fetches all enabled manga indexers
    // (Protocol == DownloadProtocol.Http filter applied by the implementation),
    // then runs Phase 5 MangaDownloadDecisionMaker.GetSearchDecision against
    // the aggregated reports.
    //
    // Returns MangaDownloadDecision (NOT TV's DownloadDecision) — Phase 5 D-05
    // ships the manga-shaped decision DTO carrying RemoteChapter; Plan 06-06's
    // dispatcher executes the manga grab path against those decisions directly.
    // Plan-pseudocode-vs-actual-API correction (Rule 1): the plan's verbatim
    // signature returned `DownloadDecision` but the Phase 5 IMakeMangaDownloadDecision
    // already returns MangaDownloadDecision; bridging through TV's
    // IProcessDownloadDecisions would require an adapter that does not exist.
    //
    // Phase 8 cleanup: collapse with ISearchForReleases when Tv/ deletes.
    public interface IMangaSearchForReleases
    {
        Task<List<MangaDownloadDecision>> MangaSearch(MangaSearchCriteria criteria);
        Task<List<MangaDownloadDecision>> ChapterSearch(ChapterSearchCriteria criteria);
    }
}
