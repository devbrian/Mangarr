using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaParserTests
{
    /// <summary>
    /// Wave 0 contract fixture for <c>MangaParsingService.Map(ParsedChapterInfo, Manga, IList&lt;Chapter&gt;)</c>.
    /// Covers D-03 mapping flow + D-10 indexer-language-wins precedence rule.
    /// RED until Plan 02-04.
    /// </summary>
    [TestFixture]
    public class MangaParsingServiceFixture : CoreTest<MangaParsingService>
    {
        private NzbDrone.Core.Manga.Manga _manga;
        private List<Chapter> _chapters;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<NzbDrone.Core.Manga.Manga>
                .CreateNew()
                .With(m => m.Title = "Naruto")
                .With(m => m.CleanTitle = "naruto")
                .Build();

            _chapters = new List<Chapter>
            {
                Builder<Chapter>
                    .CreateNew()
                    .With(c => c.MangaId = _manga.Id)
                    .With(c => c.ChapterNumber = 1m)
                    .Build(),
            };
        }

        [Test]
        public void Map_resolves_existing_chapter_via_FindByMangaAndNumber()
        {
            // Phase 16 STRUCT-01: 3-arg FindByMangaAndNumber collapsed to 2-arg.
            var parsed = new ParsedChapterInfo
            {
                ReleaseTitle = "[Group] Naruto - Ch.1 [EN]",
                MangaTitle = "Naruto",
                ChapterNumbers = new[] { 1m },
                TranslatedLanguage = "en",
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.FindByMangaAndNumber(_manga.Id, 1m))
                .Returns(_chapters[0]);

            var result = Subject.Map(parsed, _manga, _chapters);

            result.Should().NotBeNull();
            result.Manga.Should().Be(_manga);
            result.Chapters.Should().Contain(_chapters[0]);
        }

        [Test]
        public void Map_returns_null_when_manga_not_found()
        {
            var parsed = new ParsedChapterInfo
            {
                ReleaseTitle = "[Group] Unknown - Ch.1 [EN]",
                MangaTitle = "Unknown",
                ChapterNumbers = new[] { 1m },
                TranslatedLanguage = "en",
            };

            // Per D-03: if manga is null, Map returns null without resolving chapters.
            var result = Subject.Map(parsed, null, _chapters);
            result.Should().BeNull();
        }

        [Test]
        public void Map_uses_indexer_supplied_TranslatedLanguage_when_set()
        {
            // D-10: indexer-supplied wins. This test simulates the indexer pre-setting
            // TranslatedLanguage to "es" even though the release_title carries "[EN]".
            var parsed = new ParsedChapterInfo
            {
                ReleaseTitle = "[Group] Naruto - Ch.1 [EN]",
                MangaTitle = "Naruto",
                ChapterNumbers = new[] { 1m },
                TranslatedLanguage = "es",
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.FindByMangaAndNumber(_manga.Id, 1m))
                .Returns(_chapters[0]);

            var result = Subject.Map(parsed, _manga, _chapters);

            result.Should().NotBeNull();
            result.ParsedChapterInfo.TranslatedLanguage.Should().Be("es");
        }

        [Test]
        public void Map_falls_back_to_parser_TranslatedLanguage_when_indexer_omits()
        {
            // D-10 fallback: when the indexer doesn't pre-populate TranslatedLanguage,
            // the parser-extracted "en" carries through.
            var parsed = new ParsedChapterInfo
            {
                ReleaseTitle = "[Group] Naruto - Ch.1 [EN]",
                MangaTitle = "Naruto",
                ChapterNumbers = new[] { 1m },
                TranslatedLanguage = "en",
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.FindByMangaAndNumber(_manga.Id, 1m))
                .Returns(_chapters[0]);

            var result = Subject.Map(parsed, _manga, _chapters);

            result.Should().NotBeNull();
            result.ParsedChapterInfo.TranslatedLanguage.Should().Be("en");
        }

        // Issue #30 regression — when neither indexer nor parser supplied a language
        // (the disk-scan / manual-import case for a CBZ file with no language tag in the
        // filename), Map should match the chapter by (mangaId, chapterNumber).
        //
        // Phase 16 STRUCT-01 simplifies this: canonical Chapter is now language-free, so
        // the `_chapters[0].TranslatedLanguage.Should().Be("en")` post-condition is gone.
        [Test]
        public void Map_falls_back_to_chapter_number_only_when_no_language_signal()
        {
            var parsed = new ParsedChapterInfo
            {
                ReleaseTitle = "Naruto - Chapter 001",
                MangaTitle = "Naruto",
                ChapterNumbers = new[] { 1m },
                TranslatedLanguage = null,
            };

            // existingChapters carries one canonical Chapter at number 1; Map matches by number.
            var result = Subject.Map(parsed, _manga, _chapters);

            result.Should().NotBeNull();
            result.Chapters.Should().HaveCount(1);
            result.Chapters[0].Id.Should().Be(_chapters[0].Id);
        }

        // Phase 16 STRUCT-01 made the canonical Chapter UNIQUE on (MangaId, ChapterNumber);
        // multiple Chapter rows differing only by language are no longer a valid DB shape.
        // Phase 16.1 routes per-translation axes through ChapterFile (Sonarr-canonical
        // pattern). Multi-language disambiguation lives in the DecisionEngine layer where
        // the indexer's ReleaseInfo carries the language code and the spec scores the
        // candidate release against the manga's TranslationProfile. This test is therefore
        // [Ignore]'d at the MangaParsingService layer; the equivalent coverage lives in
        // DecisionEngineSpecificationFixture.
        [Test]
        [Ignore("Phase 16 STRUCT-01 + Phase 16.1 — disambiguation moved out of MangaParsingService into DecisionEngine specs")]
        public void Map_disambiguates_multi_language_candidates_via_TranslationProfile_when_no_language_signal()
        {
            // Coverage moved to DecisionEngine spec fixtures (Plan 16-04).
        }

        // ─────────────────────────────────────────────────────────────────────
        // GH #118 — GetManga 3-strategy resolution coverage. Sonarr-canonical
        // mirror of the deleted ParsingService.GetSeries multi-strategy shape
        // (commit 2832eecb^, lines 43-100). Each strategy is exercised in
        // isolation by gating the lower-strategy calls with empty / null
        // returns from the mocked IMangaService.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void GetManga_strategy1_resolves_via_FindByTitle_when_CleanTitle_matches()
        {
            // Direct CleanTitle match wins; Strategy 2 / 3 are not consulted.
            var mangaService = Mocker.GetMock<IMangaService>();
            mangaService.Setup(s => s.FindByTitle(It.IsAny<string>())).Returns(_manga);

            var result = Subject.GetManga("Naruto");

            result.Should().Be(_manga);
            mangaService.Verify(s => s.FindByTitle(It.IsAny<string>()), Times.Once);
            mangaService.Verify(s => s.FindByAlternativeTitle(It.IsAny<string>()), Times.Never);
            mangaService.Verify(s => s.FindByTitleInexact(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void GetManga_strategy2_falls_through_to_FindByAlternativeTitle_when_CleanTitle_misses()
        {
            // DEF-19-02-01 regression preservation: MangaDex romanized
            // "Shingeki no Kyojin" doesn't normalize-match the English-stored
            // Manga.CleanTitle "attack on titan", but DOES match an entry in
            // AlternativeTitles populated from attributes.altTitles.
            var mangaService = Mocker.GetMock<IMangaService>();
            mangaService.Setup(s => s.FindByTitle(It.IsAny<string>())).Returns((NzbDrone.Core.Manga.Manga)null);
            mangaService.Setup(s => s.FindByAlternativeTitle(It.IsAny<string>())).Returns(_manga);

            var result = Subject.GetManga("Shingeki no Kyojin");

            result.Should().Be(_manga);
            mangaService.Verify(s => s.FindByTitle(It.IsAny<string>()), Times.Once);
            mangaService.Verify(s => s.FindByAlternativeTitle(It.IsAny<string>()), Times.Once);
            mangaService.Verify(s => s.FindByTitleInexact(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void GetManga_strategy3_falls_through_to_FindByTitleInexact_when_both_higher_strategies_miss()
        {
            // Substring fallback: release title contains the manga's
            // CleanTitle but isn't an exact match (e.g. "[GroupName] Naruto - Chapter 042").
            // FindByTitleInexact returns exactly one candidate → resolved.
            var mangaService = Mocker.GetMock<IMangaService>();
            mangaService.Setup(s => s.FindByTitle(It.IsAny<string>())).Returns((NzbDrone.Core.Manga.Manga)null);
            mangaService.Setup(s => s.FindByAlternativeTitle(It.IsAny<string>())).Returns((NzbDrone.Core.Manga.Manga)null);
            mangaService.Setup(s => s.FindByTitleInexact(It.IsAny<string>()))
                .Returns(new List<NzbDrone.Core.Manga.Manga> { _manga });

            var result = Subject.GetManga("[GroupName] Naruto - Chapter 042");

            result.Should().Be(_manga);
            mangaService.Verify(s => s.FindByTitleInexact(It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void GetManga_returns_null_when_FindByTitleInexact_yields_multiple_candidates()
        {
            // Ambiguous resolution → null. Matches Sonarr's posture: when
            // GetSeries can't pick exactly one, MapInexact returns null and
            // the DownloadDecisionMaker treats null as UnknownManga rather
            // than arbitrarily picking one.
            var mangaService = Mocker.GetMock<IMangaService>();
            mangaService.Setup(s => s.FindByTitle(It.IsAny<string>())).Returns((NzbDrone.Core.Manga.Manga)null);
            mangaService.Setup(s => s.FindByAlternativeTitle(It.IsAny<string>())).Returns((NzbDrone.Core.Manga.Manga)null);

            var manga2 = Builder<NzbDrone.Core.Manga.Manga>
                .CreateNew()
                .With(m => m.Id = _manga.Id + 1)
                .With(m => m.Title = "Naruto Spin-Off")
                .With(m => m.CleanTitle = "naruto spin off")
                .Build();
            mangaService.Setup(s => s.FindByTitleInexact(It.IsAny<string>()))
                .Returns(new List<NzbDrone.Core.Manga.Manga> { _manga, manga2 });

            var result = Subject.GetManga("Naruto");

            result.Should().BeNull("ambiguous inexact resolution returns null; caller handles as UnknownManga");
        }

        [Test]
        public void GetManga_returns_null_when_all_strategies_miss()
        {
            var mangaService = Mocker.GetMock<IMangaService>();
            mangaService.Setup(s => s.FindByTitle(It.IsAny<string>())).Returns((NzbDrone.Core.Manga.Manga)null);
            mangaService.Setup(s => s.FindByAlternativeTitle(It.IsAny<string>())).Returns((NzbDrone.Core.Manga.Manga)null);
            mangaService.Setup(s => s.FindByTitleInexact(It.IsAny<string>()))
                .Returns(new List<NzbDrone.Core.Manga.Manga>());

            var result = Subject.GetManga("Completely Unknown Title");

            result.Should().BeNull();
        }
    }
}
