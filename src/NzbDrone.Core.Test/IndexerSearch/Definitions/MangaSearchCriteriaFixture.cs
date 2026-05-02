using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.Test.IndexerSearch.Definitions
{
    /// <summary>
    /// Wave 0 stub fixture for the manga search-criteria contract (D-05 / D-06). References
    /// NOT-YET-BUILT production types (lands in Plan 03-02).
    ///
    /// Locked contract:
    /// - <c>MangaSearchCriteriaBase</c> is a PARALLEL hierarchy — does NOT inherit from
    ///   <see cref="SearchCriteriaBase"/> (D-06: keep TV Series/SceneTitles fields out of manga
    ///   code paths).
    /// - <c>ChapterSearchCriteria.ChapterNumber</c> is <see cref="decimal"/> — Phase 2 D-12 widen.
    /// - <c>MangaSearchCriteria.PreferredLanguages</c> is advisory/optional (D-07; null is valid;
    ///   Phase 5 TranslationProfile is the authoritative ordering).
    /// </summary>
    [TestFixture]
    public class MangaSearchCriteriaFixture
    {
        [Test]
        public void MangaSearchCriteriaBase_does_not_inherit_from_SearchCriteriaBase()
        {
            // D-06: parallel hierarchy — no inheritance, so TV Series / SceneTitles fields stay
            // out of manga code paths.
            typeof(MangaSearchCriteriaBase).IsSubclassOf(typeof(SearchCriteriaBase)).Should().BeFalse();
        }

        [Test]
        public void ChapterSearchCriteria_ChapterNumber_is_decimal()
        {
            // Phase 2 D-12: ChapterNumber widened to DECIMAL(10,3); manga supports 12.5, 123.5
            var sc = new ChapterSearchCriteria
            {
                Manga = new Manga.Manga { Title = "One Piece" },
                Chapters = new List<Chapter>
                {
                    new Chapter { ChapterNumber = 12.5m, TranslatedLanguage = "en" }
                }
            };

            sc.ChapterNumber.Should().Be(12.5m);
            sc.TranslatedLanguage.Should().Be("en");
        }

        [Test]
        public void PreferredLanguages_is_advisory_optional()
        {
            // D-07: optional list; null is valid (Phase 5 TranslationProfile is authoritative)
            var sc = new MangaSearchCriteria { Manga = new Manga.Manga { Title = "Berserk" } };
            sc.PreferredLanguages.Should().BeNull();
        }

        [Test]
        public void MangaSearchCriteria_ToString_includes_manga_title()
        {
            var sc = new MangaSearchCriteria { Manga = new Manga.Manga { Title = "One Piece" } };
            sc.ToString().Should().Contain("One Piece");
        }
    }
}
