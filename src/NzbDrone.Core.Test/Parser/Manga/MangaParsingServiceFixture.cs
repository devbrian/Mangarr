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

        // Issue #30 regression — when neither indexer nor parser supplied a language
        // (the disk-scan / manual-import case for a CBZ file with no language tag in the
        // filename), Map should match the chapter by (mangaId, chapterNumber) regardless
        // of the DB chapter's TranslatedLanguage, instead of strict-matching against the
        // "und" sentinel and silently failing.
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

            // existingChapters carries a single English chapter — the previous strict-equality
            // path against "und" silently failed; the fallback path should match by number.
            var result = Subject.Map(parsed, _manga, _chapters);

            result.Should().NotBeNull();
            result.Chapters.Should().HaveCount(1);
            result.Chapters[0].Id.Should().Be(_chapters[0].Id);
            result.Chapters[0].TranslatedLanguage.Should().Be("en");
        }

        // Issue #30 regression — when multiple language candidates exist for the same
        // (mangaId, chapterNumber) and the parser supplied no language, prefer the
        // chapter whose language ranks earliest in the manga's TranslationProfile.
        [Test]
        public void Map_disambiguates_multi_language_candidates_via_TranslationProfile_when_no_language_signal()
        {
            _manga.TranslationProfileId = 7;

            var enChapter = Builder<Chapter>
                .CreateNew()
                .With(c => c.Id = 101)
                .With(c => c.MangaId = _manga.Id)
                .With(c => c.ChapterNumber = 1m)
                .With(c => c.TranslatedLanguage = "en")
                .Build();
            var esChapter = Builder<Chapter>
                .CreateNew()
                .With(c => c.Id = 102)
                .With(c => c.MangaId = _manga.Id)
                .With(c => c.ChapterNumber = 1m)
                .With(c => c.TranslatedLanguage = "es")
                .Build();

            var existing = new List<Chapter> { esChapter, enChapter };

            Mocker.GetMock<NzbDrone.Core.Profiles.Translations.ITranslationProfileService>()
                .Setup(s => s.Get(7))
                .Returns(new NzbDrone.Core.Profiles.Translations.TranslationProfile
                {
                    Id = 7,
                    Languages = new List<string> { "en", "es" }
                });

            var parsed = new ParsedChapterInfo
            {
                ReleaseTitle = "Naruto - Chapter 001",
                MangaTitle = "Naruto",
                ChapterNumbers = new[] { 1m },
                TranslatedLanguage = null,
            };

            var result = Subject.Map(parsed, _manga, existing);

            result.Should().NotBeNull();
            result.Chapters.Should().HaveCount(1);

            // "en" ranks first in the profile, so the en chapter wins even though es
            // appeared first in the existingChapters list.
            result.Chapters[0].Id.Should().Be(enChapter.Id);
        }
    }
}
