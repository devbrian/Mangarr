using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.Test.OrganizerTests.Manga
{
    // Phase 5 plan 05-06 — replaces the Wave-0 placeholder fixture. Covers the D-14 token set + Pitfall 7
    // (culture-invariant decimal padding) per pattern S10. Each test sets a NamingConfig
    // with the test's StandardChapterFormat, builds a manga + chapter + release, and asserts
    // Subject.BuildFileName(...).
    [TestFixture]
    public class MangaFileNameBuilderFixture : CoreTest<MangaFileNameBuilder>
    {
        private NamingConfig _namingConfig;
        private MangaModel _manga;

        [SetUp]
        public void Setup()
        {
            // Use Komga preset shape as the baseline; individual tests overwrite StandardChapterFormat.
            _namingConfig = NamingConfig.Default;
            _namingConfig.StandardChapterFormat = "{Manga.Title} - Chapter {Chapter.Number:000}";
            _namingConfig.MangaFolderFormat = "{Manga.Title}";
            _namingConfig.RenameChapters = true;
            _namingConfig.ReplaceIllegalCharacters = true;
            _namingConfig.ColonReplacementFormat = 0; // Sonarr divergence: Plan 15-10 retyped enum -> int (Smart=0)

            Mocker.GetMock<INamingConfigService>()
                  .Setup(s => s.GetConfig())
                  .Returns(_namingConfig);

            _manga = new MangaModel
            {
                Id = 1,
                Title = "Solo Leveling"
            };
        }

        private List<NzbDrone.Core.Manga.Chapter> SingleChapter(decimal number, string title = null, string lang = null, string scanGroup = null)
        {
            // Phase 16 STRUCT-01 + Phase 16.1: TranslatedLanguage / ScanlationGroup are
            // language/group axes on ChapterFile (post-import) per the Sonarr-canonical
            // pattern (mirrors EpisodeFile.Languages + EpisodeFile.ReleaseGroup placement).
            // Token resolution reads from ReleaseInfo at filename-builder time. The lang /
            // scanGroup args are kept for caller compatibility but unused here; tests that
            // assert {Language} or {ScanlationGroup} tokens populate the ReleaseInfo
            // fixture directly.
            _ = lang;
            _ = scanGroup;
            return new List<NzbDrone.Core.Manga.Chapter>
            {
                new NzbDrone.Core.Manga.Chapter
                {
                    Id = 100,
                    MangaId = 1,
                    ChapterNumber = number,
                    Title = title,
                }
            };
        }

        // ── D-14 token resolution tests ─────────────────────────────────────────────────────

        [Test]
        public void Resolves_Manga_Title_token()
        {
            _namingConfig.StandardChapterFormat = "{Manga.Title}";
            _manga.Title = "Solo Leveling";

            var name = Subject.BuildFileName(SingleChapter(1), _manga);

            name.Should().Be("Solo Leveling");
        }

        [Test]
        public void Resolves_Chapter_Number_with_3_digit_padding()
        {
            _namingConfig.StandardChapterFormat = "Chapter {Chapter.Number:000}";

            var name = Subject.BuildFileName(SingleChapter(42m), _manga);

            name.Should().Be("Chapter 042");
        }

        [Test]
        public void Resolves_Chapter_Number_with_4_digit_padding()
        {
            _namingConfig.StandardChapterFormat = "Ch.{Chapter.Number:0000}";

            var name = Subject.BuildFileName(SingleChapter(42m), _manga);

            name.Should().Be("Ch.0042");
        }

        [Test]
        public void Resolves_Chapter_Number_decimal_padding_format()
        {
            _namingConfig.StandardChapterFormat = "Chapter {Chapter.Number:000.0}";

            var name = Subject.BuildFileName(SingleChapter(42.5m), _manga);

            name.Should().Be("Chapter 042.5");
        }

        [Test]
        public void Resolves_Manga_MangaDexId_token()
        {
            _namingConfig.StandardChapterFormat = "{Manga.MangaDexId}";
            _manga.MangaDexId = Guid.Parse("d8a959f7-1111-2222-3333-444455556666");

            var name = Subject.BuildFileName(SingleChapter(1), _manga);

            name.Should().Be("d8a959f7-1111-2222-3333-444455556666");
        }

        [Test]
        public void Resolves_Manga_MalId_token()
        {
            _namingConfig.StandardChapterFormat = "{Manga.MalId}";
            _manga.MalId = 12345;

            var name = Subject.BuildFileName(SingleChapter(1), _manga);

            name.Should().Be("12345");
        }

        [Test]
        public void Resolves_Manga_AniListId_token()
        {
            _namingConfig.StandardChapterFormat = "{Manga.AniListId}";
            _manga.AniListId = 67890;

            var name = Subject.BuildFileName(SingleChapter(1), _manga);

            name.Should().Be("67890");
        }

        [Test]
        public void Resolves_ScanlationGroup_token_from_release()
        {
            _namingConfig.StandardChapterFormat = "[{ScanlationGroup}]";
            var release = new ReleaseInfo { ScanlationGroup = "AsuraScans" };

            var name = Subject.BuildFileName(SingleChapter(1), _manga, release);

            name.Should().Be("[AsuraScans]");
        }

        [Test]
        public void Resolves_Language_token_from_release()
        {
            _namingConfig.StandardChapterFormat = "[{Language}]";
            var release = new ReleaseInfo { TranslatedLanguage = "en" };

            var name = Subject.BuildFileName(SingleChapter(1), _manga, release);

            name.Should().Be("[en]");
        }

        [Test]
        public void Resolves_Source_token_from_release_indexer()
        {
            _namingConfig.StandardChapterFormat = "[{Source}]";
            var release = new ReleaseInfo { Indexer = "mangadex" };

            var name = Subject.BuildFileName(SingleChapter(1), _manga, release);

            name.Should().Be("[mangadex]");
        }

        // ── Pattern S10 — Pitfall 7 culture-invariant padding regression cell ──────────────

        [Test]
        [SetCulture("de-DE")]
        public void Chapter_number_padding_is_culture_invariant_per_S10_pattern()
        {
            // Without InvariantCulture, German locale would render "0042,5" (or "042,5") because
            // the comma is the decimal separator. Reader apps lex-sort by string and "042,5"
            // sorts incorrectly relative to "043". This test pins the canonical "042.5" output.
            _namingConfig.StandardChapterFormat = "Chapter {Chapter.Number:000.0}";

            var name = Subject.BuildFileName(SingleChapter(42.5m), _manga);

            name.Should().Contain("Chapter 042.5");
            name.Should().NotContain("042,5");
            name.Should().NotContain("0042,5");
        }

        [Test]
        public void Komga_preset_full_round_trip()
        {
            _namingConfig.StandardChapterFormat = "{Manga.Title} - Chapter {Chapter.Number:000}";
            _manga.Title = "Test";

            var name = Subject.BuildFileName(SingleChapter(42m), _manga);

            name.Should().Be("Test - Chapter 042");
        }

        // ── Folder rendering ─────────────────────────────────────────────────────────────

        [Test]
        public void GetMangaFolder_resolves_Manga_Title_per_D15_flat_layout()
        {
            _namingConfig.MangaFolderFormat = "{Manga.Title}";
            _manga.Title = "Solo Leveling";

            var folder = Subject.GetMangaFolder(_manga);

            folder.Should().Be("Solo Leveling");
        }
    }
}
