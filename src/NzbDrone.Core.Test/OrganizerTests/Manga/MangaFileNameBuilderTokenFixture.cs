using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.Test.Framework;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.Test.OrganizerTests.Manga
{
    // Phase 30 Plan 30-05 Task 5 (II2-03) — Wave 0 token fixture for the 3 new
    // MediaInfo-backed naming tokens: {Page Count}, {Color}, {DPI}.
    //
    // Mirrors the SetUp shape of MangaFileNameBuilderFixture (Phase 5 plan 05-06),
    // but stubs IChapterFileRepository.GetFilesByChapter so each [Test] can supply
    // a per-case MediaInfo blob to exercise the token resolver's null-skip per D-09.
    [TestFixture]
    public class MangaFileNameBuilderTokenFixture : CoreTest<MangaFileNameBuilder>
    {
        private NamingConfig _namingConfig;
        private MangaModel _manga;
        private List<NzbDrone.Core.Manga.Chapter> _chapters;

        [SetUp]
        public void Setup()
        {
            _namingConfig = NamingConfig.Default;
            _namingConfig.MangaFolderFormat = "{Manga.Title}";
            _namingConfig.RenameChapters = true;
            _namingConfig.ReplaceIllegalCharacters = true;
            _namingConfig.ColonReplacementFormat = 0;

            Mocker.GetMock<INamingConfigService>()
                  .Setup(s => s.GetConfig())
                  .Returns(_namingConfig);

            _manga = new MangaModel { Id = 1, Title = "Berserk" };
            _chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new NzbDrone.Core.Manga.Chapter { Id = 100, MangaId = 1, ChapterNumber = 132m }
            };
        }

        private void StubMediaInfo(ChapterMediaInfo info)
        {
            // GetChapterFileMediaInfo reads _chapterFileRepository.GetFilesByChapter(chapter.Id)
            // and projects .FirstOrDefault()?.MediaInfo — supply 1 file row carrying `info`.
            var file = new ChapterFile
            {
                Id = 500,
                MangaId = 1,
                ChapterId = 100,
                MediaInfo = info
            };
            Mocker.GetMock<IChapterFileRepository>()
                  .Setup(r => r.GetFilesByChapter(100))
                  .Returns(new List<ChapterFile> { file });
        }

        // ============================================================
        // {Page Count}
        // ============================================================
        [Test]
        public void should_render_page_count_when_MediaInfo_populated()
        {
            StubMediaInfo(new ChapterMediaInfo { PageCount = 24 });
            _namingConfig.StandardChapterFormat = "Pages={Page Count}";

            var name = Subject.BuildFileName(_chapters, _manga);

            name.Should().Contain("24");
        }

        [Test]
        public void should_skip_page_count_when_MediaInfo_null()
        {
            // No StubMediaInfo => GetFilesByChapter returns null/empty => helper returns null.
            // D-09: the entire `{Page Count}` token (including any prefix/suffix separators
            // captured by TitleRegex inside the braces) collapses to empty string; literal
            // characters outside the braces stay verbatim.
            _namingConfig.StandardChapterFormat = "Berserk - {Page Count}";

            var name = Subject.BuildFileName(_chapters, _manga);

            name.Should().NotContain("0");
            name.Should().NotContain("Unknown");

            // FileNameCleanupRegex collapses duplicate separators; TrimSeparatorsRegex strips
            // trailing [- ._]. After the null token collapses, "Berserk - " trims to "Berserk".
            name.Should().Be("Berserk");
        }

        // ============================================================
        // {Color}
        // ============================================================
        [Test]
        public void should_render_color_text_when_color_true()
        {
            StubMediaInfo(new ChapterMediaInfo { Color = true });
            _namingConfig.StandardChapterFormat = "{Color}";

            var name = Subject.BuildFileName(_chapters, _manga);

            name.Should().Be("Color");
        }

        [Test]
        public void should_render_bw_text_when_color_false()
        {
            StubMediaInfo(new ChapterMediaInfo { Color = false });
            _namingConfig.StandardChapterFormat = "{Color}";

            var name = Subject.BuildFileName(_chapters, _manga);

            // Exact assertion catches regressions (e.g. naming-config colon-replacement
            // mangling `&` to a different separator) that Contain("B")+Contain("W") would
            // silently miss. The token resolver at MangaFileNameBuilder.cs:245 returns
            // the literal "B&W" string for Color = false.
            name.Should().Be("B&W");
        }

        [Test]
        public void should_skip_color_when_null()
        {
            StubMediaInfo(new ChapterMediaInfo { Color = null });
            _namingConfig.StandardChapterFormat = "Berserk-{Color}";

            var name = Subject.BuildFileName(_chapters, _manga);

            name.Should().Be("Berserk");
        }

        // ============================================================
        // {DPI}
        // ============================================================
        [Test]
        public void should_render_dpi_when_populated()
        {
            StubMediaInfo(new ChapterMediaInfo { DpiHorizontal = 300 });
            _namingConfig.StandardChapterFormat = "{DPI}";

            var name = Subject.BuildFileName(_chapters, _manga);

            name.Should().Be("300");
        }

        [Test]
        public void should_skip_dpi_when_null()
        {
            // R-4: ImageSharp default-96 case — DpiHorizontal stays null; token renders empty.
            StubMediaInfo(new ChapterMediaInfo { PageCount = 12, Color = true, DpiHorizontal = null });
            _namingConfig.StandardChapterFormat = "Berserk-{DPI}";

            var name = Subject.BuildFileName(_chapters, _manga);

            name.Should().Be("Berserk");
        }

        // ============================================================
        // ALL null = entire MediaInfo blob is null (pre-Migration-004 row)
        // ============================================================
        [Test]
        public void should_skip_all_tokens_when_MediaInfo_blob_is_null()
        {
            // GetFilesByChapter returns a ChapterFile with MediaInfo = null.
            var file = new ChapterFile { Id = 500, MangaId = 1, ChapterId = 100, MediaInfo = null };
            Mocker.GetMock<IChapterFileRepository>()
                  .Setup(r => r.GetFilesByChapter(100))
                  .Returns(new List<ChapterFile> { file });

            _namingConfig.StandardChapterFormat = "Berserk-{Page Count}-{Color}-{DPI}";

            var name = Subject.BuildFileName(_chapters, _manga);

            name.Should().Be("Berserk");
        }
    }
}
