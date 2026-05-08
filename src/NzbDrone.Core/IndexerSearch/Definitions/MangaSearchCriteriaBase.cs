using System.Collections.Generic;

// Sonarr divergence: Phase 15 Plan 15-04 cascade absorption — DataAugmentation/ DELETED;
// SearchMode enum no longer exists. Manga search criteria do not need SearchMode (TV
// distinguishes default vs. RSS vs. interactive; manga uses MonitoredChaptersOnly +
// UserInvokedSearch + InteractiveSearch flags directly).

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    /// <summary>
    /// Base type for manga indexer search criteria (D-06). Parallel hierarchy to
    /// <c>SearchCriteriaBase</c> (DELETED Phase 15): NO inheritance — that base carries TV-shaped
    /// <c>Series</c> / <c>SceneTitles</c> / <c>Episodes</c> fields that would leak into manga code.
    /// Phase 8 collapses both bases when <c>Tv/</c> deletes.
    /// </summary>
    public abstract class MangaSearchCriteriaBase
    {
        /// <summary>The manga being searched.</summary>
        // Fully-qualified: the new IndexerSearch.Manga namespace (Plan 06-06) shadows
        // the bare `Manga.` prefix because C#'s nearest-enclosing-namespace lookup
        // finds `IndexerSearch.Manga` first when this file (in IndexerSearch.Definitions)
        // is compiled. Fully-qualifying via the global root keeps the type unambiguous.
        public NzbDrone.Core.Manga.Manga Manga { get; set; }

        /// <summary>The chapters under search (whole-manga = list of monitored; single-chapter = [target]).</summary>
        public List<NzbDrone.Core.Manga.Chapter> Chapters { get; set; }

        /// <summary>
        /// D-07: ADVISORY only. Optional list of BCP-47 codes (e.g. <c>["en", "es"]</c>).
        /// Indexers MAY use it to scope queries when their API supports it (e.g. MangaDex
        /// <c>/chapter?translatedLanguage[]=en</c>) for bandwidth savings; the Phase 5
        /// <c>TranslationProfile</c> ordinal gate has FINAL SAY at decision time.
        /// </summary>
        public IReadOnlyList<string> PreferredLanguages { get; set; }

        // Sonarr divergence: Phase 15 Plan 15-04 — SearchMode (DataAugmentation/) stripped.
        public virtual bool MonitoredChaptersOnly { get; set; }
        public virtual bool UserInvokedSearch { get; set; }
        public virtual bool InteractiveSearch { get; set; }
    }
}
