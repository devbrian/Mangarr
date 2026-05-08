using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.DecisionEngine;

// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape SearchCriteriaBase
// field stripped per Plan 15-10 IndexerSearch/Definitions DELETE; manga MangaSearchCriteria
// is the canonical surface. Phase 5 D-05 — manga-side criteria carrier (Rule 2 substrate fix).
public class ReleaseDecisionInformation
{
    public bool PushedRelease { get; set; }
    public MangaSearchCriteriaBase MangaSearchCriteria { get; set; }

    public ReleaseDecisionInformation()
    {
        PushedRelease = false;
        MangaSearchCriteria = null;
    }

    // Phase 5 D-05 — manga factory.
    public static ReleaseDecisionInformation FromMangaSearch(bool pushedRelease, MangaSearchCriteriaBase mangaSearchCriteria)
    {
        return new ReleaseDecisionInformation
        {
            PushedRelease = pushedRelease,
            MangaSearchCriteria = mangaSearchCriteria
        };
    }
}
