using System.Linq;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    /// <summary>
    /// Single-chapter search — fetch a specific chapter from configured sources.
    /// Equivalent role to Sonarr's <see cref="SingleEpisodeSearchCriteria"/> on the manga side (D-05).
    /// <c>Chapters[0]</c> carries the targeted chapter; computed properties expose its identifying
    /// fields. ChapterNumber is <c>decimal</c> per Phase 2 D-12 (DECIMAL(10,3) widen) — manga
    /// supports 12.5, 123.5, 1.123 forms.
    /// </summary>
    public class ChapterSearchCriteria : MangaSearchCriteriaBase
    {
        public decimal ChapterNumber => Chapters?.FirstOrDefault()?.ChapterNumber ?? 0m;
        public string TranslatedLanguage => Chapters?.FirstOrDefault()?.TranslatedLanguage;

        public override string ToString()
            => $"[{Manga?.Title} : Ch.{ChapterNumber:0.###} {TranslatedLanguage}]";
    }
}
