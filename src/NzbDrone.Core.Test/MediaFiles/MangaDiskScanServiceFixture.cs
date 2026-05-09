using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    using Manga = NzbDrone.Core.Manga.Manga;

    // Sonarr divergence: NEW manga sibling fixture per Phase 9 D-09-03 #1.
    // Role-match analog: src/NzbDrone.Core.Test/MediaFiles/DiskScanServiceTests/ScanFixture.cs.
    [TestFixture]
    public class MangaDiskScanServiceFixture : CoreTest<MangaDiskScanService>
    {
        private Manga _manga;
        private string _rootFolder;
        private string _otherMangaFolder;

        [SetUp]
        public void Setup()
        {
            _rootFolder = @"C:\Test\Manga".AsOsAgnostic();
            _otherMangaFolder = @"C:\Test\Manga\OtherManga".AsOsAgnostic();
            var mangaFolder = @"C:\Test\Manga\MangaTitle".AsOsAgnostic();

            _manga = Builder<Manga>.CreateNew()
                                   .With(m => m.Path = mangaFolder)
                                   .With(m => m.Title = "Manga Title")
                                   .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(false);

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>()))
                  .Returns(_rootFolder);

            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.GetFilesByManga(It.IsAny<int>()))
                  .Returns(new List<ChapterFile>());

            Mocker.GetMock<IChapterService>()
                  .Setup(s => s.GetChaptersByManga(It.IsAny<int>()))
                  .Returns(new List<Chapter>());

            Mocker.GetMock<IMangaParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedChapterInfo>(), It.IsAny<Manga>(), It.IsAny<IList<Chapter>>()))
                  .Returns((RemoteChapter)null);

            Mocker.GetMock<IMakeMangaImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<LocalChapter>>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()))
                  .Returns(new List<MangaImportDecision>());

            Mocker.GetMock<IImportApprovedChapters>()
                  .Setup(s => s.Import(It.IsAny<List<MangaImportDecision>>(), It.IsAny<bool>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()))
                  .Returns(new List<MangaImportResult>());
        }

        private void GivenRootFolder(params string[] subfolders)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(_rootFolder))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetDirectories(_rootFolder))
                  .Returns(subfolders);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderEmpty(_rootFolder))
                  .Returns(subfolders.Empty());

            foreach (var folder in subfolders)
            {
                Mocker.GetMock<IDiskProvider>()
                      .Setup(s => s.FolderExists(folder))
                      .Returns(true);
            }
        }

        private void GivenMangaFolder()
        {
            GivenRootFolder(_manga.Path);
        }

        private void GivenFiles(IEnumerable<string> files)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(It.IsAny<string>(), true))
                  .Returns(files.ToArray());
        }

        [Test]
        public void should_not_scan_if_root_folder_does_not_exist()
        {
            Subject.Scan(_manga);

            ExceptionVerification.ExpectedWarns(1);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.GetFiles(_manga.Path, true), Times.Never());

            Mocker.GetMock<IMangaFileTableCleanupService>()
                  .Verify(v => v.Clean(It.IsAny<Manga>(), It.IsAny<List<string>>()), Times.Never());
        }

        [Test]
        public void should_not_scan_if_manga_root_folder_is_empty()
        {
            GivenRootFolder();

            Subject.Scan(_manga);

            ExceptionVerification.ExpectedWarns(1);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.GetFiles(_manga.Path, true), Times.Never());

            Mocker.GetMock<IMangaFileTableCleanupService>()
                  .Verify(v => v.Clean(It.IsAny<Manga>(), It.IsAny<List<string>>()), Times.Never());

            Mocker.GetMock<IMakeMangaImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<LocalChapter>>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()), Times.Never());
        }

        [Test]
        public void should_create_if_manga_folder_does_not_exist_but_create_folder_enabled()
        {
            GivenRootFolder(_otherMangaFolder);

            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.CreateEmptySeriesFolders)
                  .Returns(true);

            Subject.Scan(_manga);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.CreateFolder(_manga.Path), Times.Once());
        }

        [Test]
        public void should_clean_but_not_import_if_manga_folder_does_not_exist()
        {
            GivenRootFolder(_otherMangaFolder);

            Subject.Scan(_manga);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.FolderExists(_manga.Path), Times.Once());

            // D-09-03 #1: cleanup runs even when folder is missing (TV mirror).
            Mocker.GetMock<IMangaFileTableCleanupService>()
                  .Verify(v => v.Clean(It.IsAny<Manga>(), It.IsAny<List<string>>()), Times.Once());

            Mocker.GetMock<IMakeMangaImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<LocalChapter>>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()), Times.Never());
        }

        [Test]
        public void should_call_cleanup_before_import_decision_when_folder_exists()
        {
            GivenMangaFolder();
            GivenFiles(new List<string>
            {
                Path.Combine(_manga.Path, "Chapter1.cbz").AsOsAgnostic()
            });

            Subject.Scan(_manga);

            // D-09-03 #1: cleanup runs INSIDE Scan flow.
            Mocker.GetMock<IMangaFileTableCleanupService>()
                  .Verify(v => v.Clean(_manga, It.IsAny<List<string>>()), Times.Once());

            Mocker.GetMock<IMakeMangaImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<LocalChapter>>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()), Times.Once());

            // Issue #30 — Map mock returns null RemoteChapter, so the file ends up unmatched.
            // The Scan flow now logs a Warn for each unresolved file (instead of silently
            // dropping it through ImportApprovedChapters); assert that exactly one Warn fires.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_filter_paths_through_extension_allowlist()
        {
            GivenMangaFolder();

            GivenFiles(new List<string>
            {
                Path.Combine(_manga.Path, "Ch001.cbz").AsOsAgnostic(),
                Path.Combine(_manga.Path, "Ch002.cbr").AsOsAgnostic(),
                Path.Combine(_manga.Path, "Ch003.zip").AsOsAgnostic(),
                Path.Combine(_manga.Path, "Ch004.cb7").AsOsAgnostic(),
                Path.Combine(_manga.Path, "extra.mkv").AsOsAgnostic(),
                Path.Combine(_manga.Path, "extra.mp4").AsOsAgnostic(),
                Path.Combine(_manga.Path, "extra.txt").AsOsAgnostic()
            });

            var files = Subject.GetMangaFiles(_manga.Path);

            files.Should().HaveCount(4);
            files.Should().NotContain(f => f.EndsWith(".mkv"));
            files.Should().NotContain(f => f.EndsWith(".mp4"));
            files.Should().NotContain(f => f.EndsWith(".txt"));
        }

        [Test]
        public void should_publish_MangaScannedEvent_at_end_of_scan()
        {
            GivenMangaFolder();
            GivenFiles(new List<string>());

            Subject.Scan(_manga);

            // Pitfall 4 — event publish is the LAST line of CompletedScanning.
            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<MangaScannedEvent>(e => e.Manga == _manga)), Times.Once());
        }

        [Test]
        public void should_iterate_all_manga_when_RescanMangaCommand_has_no_MangaId()
        {
            var allManga = Builder<Manga>.CreateListOfSize(3)
                .All()
                .With(m => m.Path = _otherMangaFolder)
                .Build()
                .ToList();

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(allManga);

            // Default: folders return false → each Scan exits early via RootFolder warning.
            Subject.Execute(new RescanMangaCommand());

            // 3 mangas + the original setup expects warns; 3 root-folder warnings
            ExceptionVerification.ExpectedWarns(3);

            Mocker.GetMock<IMangaService>()
                  .Verify(v => v.GetAllManga(), Times.Once());
        }

        [Test]
        public void should_scan_specific_manga_when_RescanMangaCommand_has_MangaId()
        {
            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetManga(_manga.Id))
                  .Returns(_manga);

            Subject.Execute(new RescanMangaCommand(_manga.Id));

            ExceptionVerification.ExpectedWarns(1);

            Mocker.GetMock<IMangaService>()
                  .Verify(v => v.GetManga(_manga.Id), Times.Once());
            Mocker.GetMock<IMangaService>()
                  .Verify(v => v.GetAllManga(), Times.Never());
        }

        // Issue #30 regression — when MangaParsingService.Map resolves a chapter via the
        // language-fallback path (file dropped into manga folder with no language tag in the
        // filename), MangaDiskScanService should:
        //   1. Build a LocalChapter with non-null Chapter.
        //   2. Back-propagate the matched chapter's TranslatedLanguage onto the LocalChapter
        //      (so the downstream ChapterFile row records correct provenance).
        //   3. Forward the LocalChapter to the import-decision pipeline (no Warn drop).
        [Test]
        public void should_back_propagate_matched_chapter_language_when_parser_has_no_language_signal()
        {
            GivenMangaFolder();
            var filePath = Path.Combine(_manga.Path, "Manga Title - Chapter 001.cbz").AsOsAgnostic();
            GivenFiles(new List<string> { filePath });

            // Phase 16 STRUCT-01: TranslatedLanguage lifted off Chapter (now on ChapterRelease).
            var dbChapter = Builder<Chapter>
                .CreateNew()
                .With(c => c.MangaId = _manga.Id)
                .With(c => c.ChapterNumber = 1m)
                .Build();

            // Map returns the resolved RemoteChapter (simulating the language-fallback hit).
            Mocker.GetMock<IMangaParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedChapterInfo>(), It.IsAny<Manga>(), It.IsAny<IList<Chapter>>()))
                  .Returns(new RemoteChapter
                  {
                      Manga = _manga,
                      Chapters = new List<Chapter> { dbChapter }
                  });

            // Phase 16 STRUCT-04 + Plan 16-03: language back-propagation now reads from
            // IChapterReleaseService.GetReleasesByChapter against the matched Chapter id.
            // Stub a single ChapterRelease at "en"/Group-X so the fallback resolves.
            Mocker.GetMock<NzbDrone.Core.Manga.IChapterReleaseService>()
                  .Setup(s => s.GetReleasesByChapter(dbChapter.Id))
                  .Returns(new List<NzbDrone.Core.Manga.ChapterRelease>
                  {
                      new()
                      {
                          ChapterId = dbChapter.Id,
                          TranslatedLanguage = "en",
                          ScanlationGroup = "Group-X",
                      },
                  });

            // Capture the LocalChapters fed into the decision maker so we can assert on them.
            List<LocalChapter> captured = null;
            Mocker.GetMock<IMakeMangaImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<LocalChapter>>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()))
                  .Callback<List<LocalChapter>, NzbDrone.Core.Download.DownloadClientItem>((lcs, _) => captured = lcs)
                  .Returns(new List<MangaImportDecision>());

            Subject.Scan(_manga);

            captured.Should().NotBeNull();
            captured.Should().HaveCount(1);

            var lc = captured[0];
            lc.Chapter.Should().NotBeNull();

            // Phase 16 STRUCT-04 + Plan 16-03: when the parser extracted no language tag
            // from the filename, MangaDiskScanService falls back to the matched Chapter's
            // first ChapterRelease.TranslatedLanguage — Issue #30 back-propagation
            // re-introduced against the ChapterRelease grain.
            lc.TranslatedLanguage.Should().Be("en");
        }
    }
}
