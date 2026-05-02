using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
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
                    .With(c => c.TranslatedLanguage = "en")
                    .Build(),
            };
        }

        [Test]
        public void Map_resolves_existing_chapter_via_FindByMangaAndNumber()
        {
            var parsed = new ParsedChapterInfo
            {
                ReleaseTitle = "[Group] Naruto - Ch.1 [EN]",
                MangaTitle = "Naruto",
                ChapterNumbers = new[] { 1m },
                TranslatedLanguage = "en",
            };

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.FindByMangaAndNumber(_manga.Id, 1m, "en"))
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
                .Setup(s => s.FindByMangaAndNumber(_manga.Id, 1m, "es"))
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
                .Setup(s => s.FindByMangaAndNumber(_manga.Id, 1m, "en"))
                .Returns(_chapters[0]);

            var result = Subject.Map(parsed, _manga, _chapters);

            result.Should().NotBeNull();
            result.ParsedChapterInfo.TranslatedLanguage.Should().Be("en");
        }
    }
}
