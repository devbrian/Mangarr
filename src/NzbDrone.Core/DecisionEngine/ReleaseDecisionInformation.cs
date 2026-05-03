using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.DecisionEngine;

public class ReleaseDecisionInformation
{
    public bool PushedRelease { get; set; }
    public SearchCriteriaBase SearchCriteria { get; set; }

    // Phase 5 D-05 — manga-side criteria carrier (Rule 2 substrate fix per plan 05-04).
    // Distinct from SearchCriteria (TV) so per-pipeline specs cannot accidentally read the
    // wrong shape. Manga decision pipeline reads this; TV pipeline reads SearchCriteria.
    public MangaSearchCriteriaBase MangaSearchCriteria { get; set; }

    public ReleaseDecisionInformation()
    {
        PushedRelease = false;
        SearchCriteria = null;
        MangaSearchCriteria = null;
    }

    public ReleaseDecisionInformation(bool pushedRelease, SearchCriteriaBase searchCriteria)
    {
        PushedRelease = pushedRelease;
        SearchCriteria = searchCriteria;
    }

    // Phase 5 D-05 — manga factory (named to avoid ctor ambiguity with null SearchCriteriaBase).
    public static ReleaseDecisionInformation FromMangaSearch(bool pushedRelease, MangaSearchCriteriaBase mangaSearchCriteria)
    {
        return new ReleaseDecisionInformation
        {
            PushedRelease = pushedRelease,
            MangaSearchCriteria = mangaSearchCriteria
        };
    }
}
