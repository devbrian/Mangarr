namespace NzbDrone.Core.IndexerSearch.Definitions
{
    /// <summary>
    /// Whole-manga search — fetch latest releases for all monitored chapters of a manga.
    /// Equivalent role to Mangarr's <c>SeasonSearchCriteria</c> (DELETED Phase 15) on the manga side (D-05).
    /// Phase 6 Wanted / scheduled-poll constructs this; Phase 3 indexers consume via
    /// <see cref="NzbDrone.Core.Indexers.IIndexer.Fetch(MangaSearchCriteria)"/>.
    /// </summary>
    public class MangaSearchCriteria : MangaSearchCriteriaBase
    {
        public override string ToString() => $"[{Manga?.Title}]";
    }
}
