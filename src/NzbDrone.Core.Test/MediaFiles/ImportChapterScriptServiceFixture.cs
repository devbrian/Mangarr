using System.Collections.Generic;
using System.Collections.Specialized;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    // Phase 8 Plan 99-09 — IImportChapterScript / ImportChapterScriptService.
    [TestFixture]
    public class ImportChapterScriptServiceFixture : CoreTest<ImportChapterScriptService>
    {
        private LocalChapter BuildLocalChapter()
        {
            return new LocalChapter
            {
                Path = "C:\\manga-staging\\series\\Chapter 001.cbz",
                Size = 12_345_678,
                Manga = new NzbDrone.Core.Manga.Manga
                {
                    Id = 7,
                    Title = "Test Manga",
                    Path = "C:\\manga\\Test Manga",
                    Genres = new List<string> { "Action", "Adventure" },
                    Tags = new System.Collections.Generic.HashSet<int>()
                },
                Chapters = new List<NzbDrone.Core.Manga.Chapter>
                {
                    new() { Id = 100, ChapterNumber = 1m }
                },
                Release = new ReleaseInfo { Title = "Test.Manga.001" },
                CustomFormats = new List<NzbDrone.Core.CustomFormats.CustomFormat>(),
                CustomFormatScore = 0
            };
        }

        [Test]
        public void TryImport_returns_DeferMove_when_UseScriptImport_is_false()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.UseScriptImport)
                .Returns(false);

            var lc = BuildLocalChapter();
            var cf = new ChapterFile { Path = lc.Path };

            var decision = Subject.TryImport(lc.Path, "C:\\manga\\Test Manga\\Chapter 001.cbz", lc, cf, TransferMode.Move);

            decision.Should().Be(ScriptImportDecision.DeferMove);
            Mocker.GetMock<IProcessProvider>()
                .Verify(p => p.StartAndCapture(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>()), Times.Never);
        }

        [Test]
        public void TryImport_executes_script_and_returns_MoveComplete_on_default_output()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.UseScriptImport).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ScriptImportPath).Returns("/usr/local/bin/manga-import.sh");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ApplicationUrl).Returns(string.Empty);

            Mocker.GetMock<IProcessProvider>()
                .Setup(p => p.StartAndCapture(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>()))
                .Returns(new ProcessOutput { ExitCode = 0, Lines = new List<ProcessOutputLine>() });

            var lc = BuildLocalChapter();
            var cf = new ChapterFile { Path = lc.Path };

            var decision = Subject.TryImport(lc.Path, "C:\\manga\\Test Manga\\Chapter 001.cbz", lc, cf, TransferMode.Move);

            decision.Should().Be(ScriptImportDecision.MoveComplete);
            lc.ScriptImported.Should().BeTrue();
        }

        [Test]
        public void TryImport_throws_ScriptImportException_on_nonzero_exit()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.UseScriptImport).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ScriptImportPath).Returns("/usr/local/bin/manga-import.sh");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ApplicationUrl).Returns(string.Empty);

            Mocker.GetMock<IProcessProvider>()
                .Setup(p => p.StartAndCapture(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>()))
                .Returns(new ProcessOutput { ExitCode = 2, Lines = new List<ProcessOutputLine>() });

            var lc = BuildLocalChapter();
            var cf = new ChapterFile { Path = lc.Path };

            FluentActions
                .Invoking(() => Subject.TryImport(lc.Path, "C:\\manga\\Test Manga\\Chapter 001.cbz", lc, cf, TransferMode.Move))
                .Should().Throw<ScriptImportException>();

            ExceptionVerification.IgnoreErrors();
        }
    }
}
