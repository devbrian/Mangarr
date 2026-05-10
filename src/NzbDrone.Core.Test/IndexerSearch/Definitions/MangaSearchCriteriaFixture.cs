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
    ///   "SearchCriteriaBase (deleted with TV cascade in Plan 15-10)" (D-06: keep TV Series/SceneTitles fields out of manga
    ///   code paths).
    /// - <c>ChapterSearchCriteria.ChapterNumber</c> is <see cref="decimal"/> — Phase 2 D-12 widen.
    /// - <c>MangaSearchCriteria.PreferredLanguages</c> is advisory/optional (D-07; null is valid;
    ///   Phase 5 TranslationProfile is the authoritative ordering).
    /// </summary>
    [TestFixture]
    public class MangaSearchCriteriaFixture
    {
        [Test]
        public void ChapterSearchCriteria_ChapterNumber_is_decimal()
        {
            // Phase 2 D-12: ChapterNumber widened to DECIMAL(10,3); manga supports 12.5, 123.5
            // Phase 16 STRUCT-01 + Phase 16.1: canonical Chapter is language-free; per-translation
            // language data lives on ChapterFile (Phase 16.1 Sonarr-canonical pattern). The
            // redundant ChapterSearchCriteria.TranslatedLanguage computed property is gone;
            // per-language preference flows via MangaSearchCriteriaBase.PreferredLanguages.
            var sc = new ChapterSearchCriteria
            {
                Manga = new Manga.Manga { Title = "One Piece" },
                Chapters = new List<Chapter>
                {
                    new Chapter { ChapterNumber = 12.5m }
                }
            };

            sc.ChapterNumber.Should().Be(12.5m);
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
