using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.MediaFiles.MangaImport.Manual;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MangaImport.Manual
{
    // Phase 39 RETIRE-01 — the downloadId fast-path (formerly Plan 09-14: an
    // IChapterDownloadStateRepository lookup that overrode folder/mangaId from the in-process
    // ChapterDownloadState row) was REMOVED with the in-process download vertical. The fixture
    // now asserts the post-retirement contract: a non-null downloadId is inert in GetMediaFiles
    // and the caller-supplied folder + mangaId drive the scan unchanged (the gateway client
    // stages into the supplied folder, so the folder argument is authoritative).
    //
    // The fixture uses CallerFolder existence as the proxy for "downstream consumed the caller's
    // folder param". IMangaService.GetManga(int) is the proxy for "downstream consumed mangaId" —
    // mirrors the production GetMediaFiles → ProcessFolder call chain.
    //
    // Phase 32 CORR-05 — ctor pair-injection of IMangaDiskScanService landed in commit
    // 4ce9475df (Plan 32-05 Task 1). IMangaDiskScanService now owns the .cbz/.cbr/.zip/.cb7
    // extension allowlist (sourced from MangaFileExtensions). The fixture's [SetUp] now stubs
    // the new dep so the existing 5 downloadId fast-path tests still drive through
    // ListMangaArchives -> ProcessFolder cleanly; the new test
    // `should_filter_to_manga_archive_extensions_via_disk_scan_service` verifies the
    // GetMangaFiles + FilterPaths(filterExtras: false) pair-call (D-15 explicit param).
    //
    // TOCTOU hardening (debug session manualimport-500-flake, 2026-05-28) — the
    // `should_not_throw_when_*_file_vanishes_mid_scan` tests are the regression guard for the
    // nightly CI flake where GET /api/v5/manualimport?folder=<system temp> returned HTTP 500.
    // A transient .zip in the shared system temp dir was enumerated by ListMangaArchives then
    // deleted before the per-file IDiskProvider.GetFileSize stat; the unguarded GetFileSize in
    // ProcessFile's fallback return (outside the try/catch) threw an uncaught
    // FileNotFoundException that bubbled to the controller as a 500. The read-only preview scan
    // must degrade gracefully (Size = 0) rather than 500-ing the whole endpoint. Repo precedent:
    // commit 57dc54002 (mediacover lastWrite TOCTOU guard against the same manga GET 500 class).
    [TestFixture]
    public class ManualImportServiceFixture : CoreTest<ManualImportService>
    {
        private NzbDrone.Core.Manga.Manga _manga;
        private string _trackedFolder;
        private string _callerFolder;

        [SetUp]
        public void Setup()
        {
            _trackedFolder = @"C:\tracked\output".AsOsAgnostic();
            _callerFolder = @"C:\caller\path".AsOsAgnostic();

            _manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 42)
                .With(m => m.Path = _trackedFolder)
                .Build();

            // Default: every folder reachable by the fan-out exists so the call chain proceeds
            // to the verification points below (IMangaService.GetManga / FolderExists asserts).
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FolderExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(It.IsAny<string>()))
                .Returns(false);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFiles(It.IsAny<string>(), false))
                .Returns(Array.Empty<string>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetDirectories(It.IsAny<string>()))
                .Returns(Array.Empty<string>());

            // CORR-05 — IMangaDiskScanService default stubs. Without these the production
            // ListMangaArchives helper would NRE on the empty Moq defaults (string[] -> null,
            // List<string> -> null). Existing tests need these to drive through ProcessFolder
            // unchanged; the new GetMangaFiles invocation test re-uses these defaults.
            Mocker.GetMock<IMangaDiskScanService>()
                .Setup(s => s.GetMangaFiles(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(Array.Empty<string>());

            Mocker.GetMock<IMangaDiskScanService>()
                .Setup(s => s.FilterPaths(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                .Returns(new List<string>());

            // GetManga(int) returns _manga for any int — downstream consumes mangaId via this call.
            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(It.IsAny<int>()))
                .Returns(_manga);

            // Empty decision list — keeps the fan-out tight.
            Mocker.GetMock<IMakeMangaImportDecision>()
                .Setup(m => m.GetImportDecisions(It.IsAny<List<LocalChapter>>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<MangaImportDecision>());
        }

        // ===================== Phase 39 RETIRE-01 — downloadId is inert =====================

        [Test]
        public void GetMediaFiles_with_non_null_downloadId_uses_caller_folder_and_mangaId_unchanged()
        {
            // Post-retirement contract: a non-null downloadId no longer triggers any in-process
            // state-row lookup or folder/mangaId override. The caller-supplied folder + mangaId
            // drive the scan straight through to ProcessFolder (the gateway client stages into the
            // supplied folder, so the folder argument is authoritative for post-grab import).
            Subject.GetMediaFiles(folder: _callerFolder, downloadId: "abc123", mangaId: 99, filterExistingFiles: false);

            // Caller's mangaId == 99 consumed downstream (no override from a state row).
            Mocker.GetMock<IMangaService>()
                .Verify(s => s.GetManga(99), Times.AtLeastOnce);

            // Caller's folder consumed downstream; the (deleted) tracked StagingPath is never used.
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.FolderExists(_callerFolder), Times.AtLeastOnce);
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.FolderExists(_trackedFolder), Times.Never);
        }

        // ===================== CORR-05 — IMangaDiskScanService pair-call =====================

        [Test]
        public void should_filter_to_manga_archive_extensions_via_disk_scan_service()
        {
            // Caller folder exists (default setup), no downloadId fast-path; the production
            // flow reaches ProcessFolder -> ListMangaArchives which is now the
            // IMangaDiskScanService.GetMangaFiles + FilterPaths(filterExtras: false) pair-call.
            // GetMangaFiles passes allDirectories: false (mirrors Sonarr-canonical pattern —
            // ManualImport scans the top level of the folder only and recurses via
            // ProcessFolder subfolder fan-out).
            Subject.GetMediaFiles(folder: _callerFolder, downloadId: null, mangaId: 42, filterExistingFiles: false);

            // GetMangaFiles called with allDirectories: false (CORR-05 D-13 pair-call shape).
            Mocker.GetMock<IMangaDiskScanService>()
                .Verify(s => s.GetMangaFiles(It.IsAny<string>(), false), Times.AtLeastOnce);

            // FilterPaths called with filterExtras: false (CORR-05 D-15 explicit param —
            // documents manga's no-Extras-subtree divergence at the call site).
            Mocker.GetMock<IMangaDiskScanService>()
                .Verify(s => s.FilterPaths(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), false), Times.AtLeastOnce);
        }

        // ===================== TOCTOU race — file vanishes mid-scan =====================
        // Regression guard for the manualimport-500-flake nightly CI flake. A candidate
        // archive enumerated by ListMangaArchives is deleted before its per-file GetFileSize
        // stat; GetFileSize throws FileNotFoundException. The scan MUST degrade gracefully
        // (no throw, Size = 0) rather than 500-ing the GET /api/v5/manualimport endpoint.

        private string ArrangeFolderScanWithSingleArchive(string vanishedFile)
        {
            // Drive the manga==null branch of ProcessFolder so the per-file ProcessFile path
            // (including its TOCTOU-prone fallback return) is exercised. No mangaId, no
            // downloadId; the parser can't resolve a manga from the folder/file name, so the
            // file falls through ProcessFile to the fallback return that stats the file.
            Mocker.GetMock<IMangaDiskScanService>()
                .Setup(s => s.GetMangaFiles(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(new[] { vanishedFile });

            Mocker.GetMock<IMangaDiskScanService>()
                .Setup(s => s.FilterPaths(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                .Returns(new List<string> { vanishedFile });

            // No manga resolves for this folder/file — keep ProcessFile in the unknown-manga
            // branch so the GetFileSize call sites are reached.
            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(It.IsAny<int>()))
                .Returns((NzbDrone.Core.Manga.Manga)null);

            // The file vanished between enumeration and stat → GetFileSize throws.
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFileSize(vanishedFile))
                .Throws(new FileNotFoundException("File doesn't exist: " + vanishedFile));

            return vanishedFile;
        }

        [Test]
        public void should_not_throw_when_archive_file_vanishes_mid_scan()
        {
            var vanishedFile = Path.Combine(_callerFolder, "transient.zip");
            ArrangeFolderScanWithSingleArchive(vanishedFile);

            Action act = () => Subject.GetMediaFiles(folder: _callerFolder, downloadId: null, mangaId: null, filterExistingFiles: false);

            act.Should().NotThrow("a file that vanishes mid-scan must not 500 the manualimport endpoint");
        }

        [Test]
        public void should_report_size_zero_when_archive_file_vanishes_mid_scan()
        {
            var vanishedFile = Path.Combine(_callerFolder, "transient.zip");
            ArrangeFolderScanWithSingleArchive(vanishedFile);

            var result = Subject.GetMediaFiles(folder: _callerFolder, downloadId: null, mangaId: null, filterExistingFiles: false);

            result.Should().HaveCount(1);
            result.Single().Size.Should().Be(0, "a vanished file degrades to size 0 instead of throwing");
        }

        [Test]
        public void should_not_throw_when_single_file_path_vanishes_between_existence_check_and_stat()
        {
            // The single-file shape of GetMediaFiles: FolderExists false + FileExists true →
            // ProcessFile is invoked directly. The file then vanishes before the per-file stat.
            var vanishedFile = Path.Combine(_callerFolder, "single.zip");

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FolderExists(vanishedFile))
                .Returns(false);
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(vanishedFile))
                .Returns(true);

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(It.IsAny<int>()))
                .Returns((NzbDrone.Core.Manga.Manga)null);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFileSize(vanishedFile))
                .Throws(new FileNotFoundException("File doesn't exist: " + vanishedFile));

            Action act = () => Subject.GetMediaFiles(folder: vanishedFile, downloadId: null, mangaId: null, filterExistingFiles: false);

            act.Should().NotThrow("a single-file probe whose file vanishes must not 500 the endpoint");
        }
    }
}
