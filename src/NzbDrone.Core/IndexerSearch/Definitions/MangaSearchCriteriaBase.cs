using System.Collections.Generic;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    /// <summary>
    /// Base type for manga indexer search criteria (D-06). Parallel hierarchy to
    /// <see cref="SearchCriteriaBase"/>: NO inheritance — that base carries TV-shaped
    /// <c>Series</c> / <c>SceneTitles</c> / <c>Episodes</c> fields that would leak into manga code.
    /// Phase 8 collapses both bases when <c>Tv/</c> deletes.
    /// </summary>
    public abstract class MangaSearchCriteriaBase
    {
        /// <summary>The manga being searched.</summary>
        public Manga.Manga Manga { get; set; }

        /// <summary>The chapters under search (whole-manga = list of monitored; single-chapter = [target]).</summary>
        public List<Manga.Chapter> Chapters { get; set; }

        /// <summary>
        /// D-07: ADVISORY only. Optional list of BCP-47 codes (e.g. <c>["en", "es"]</c>).
        /// Indexers MAY use it to scope queries when their API supports it (e.g. MangaDex
        /// <c>/chapter?translatedLanguage[]=en</c>) for bandwidth savings; the Phase 5
        /// <c>TranslationProfile</c> ordinal gate has FINAL SAY at decision time.
        /// </summary>
        public IReadOnlyList<string> PreferredLanguages { get; set; }

        public SearchMode SearchMode { get; set; }
        public virtual bool MonitoredChaptersOnly { get; set; }
        public virtual bool UserInvokedSearch { get; set; }
        public virtual bool InteractiveSearch { get; set; }
    }
}
