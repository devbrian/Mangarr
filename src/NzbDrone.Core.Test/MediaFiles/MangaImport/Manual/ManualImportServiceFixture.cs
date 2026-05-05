using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.MediaFiles.MangaImport.Manual;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MangaImport.Manual
{
    // Phase 9 Plan 09-14 (sub-wave A 09-05 audit gap-06 close-out) — fixture asserts the
    // downloadId fast-path semantics added to MangaImport.Manual.ManualImportService:
    //   1. Non-null downloadId + existing state row → folder overridden with stateRow.StagingPath
    //      AND mangaId seeded from stateRow.MangaId when caller's mangaId is null.
    //   2. Non-null downloadId + existing state row + caller-supplied mangaId → caller's
    //      mangaId WINS (intentional manual-override semantics).
    //   3. Non-null downloadId + NULL state row → silent fast-path skip; folder + mangaId
    //      remain as caller passed (NOT an error path).
    //   4. NULL downloadId → fast-path entirely skipped (zero FindByDownloadId calls).
    //   5. Non-null state row with NULL StagingPath → only mangaId seeded; folder NOT overwritten
    //      (verifies the StagingPath.IsNotNullOrWhiteSpace() guard).
    //
    // The fixture uses CallerFolder existence as the proxy for "downstream consumed the caller's
    // folder param" and TrackedFolder existence as the proxy for "downstream consumed the
    // overridden folder from the state row". IMangaService.GetManga(int) is the proxy for
    // "downstream consumed mangaId" — mirrors the production GetMediaFiles → ProcessFolder
    // call chain at MangaImport/Manual/ManualImportService.cs:189-191.
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

            // GetManga(int) returns _manga for any int — downstream consumes mangaId via this call.
            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(It.IsAny<int>()))
                .Returns(_manga);

            // Empty decision list — keeps the fan-out tight.
            Mocker.GetMock<IMakeMangaImportDecision>()
                .Setup(m => m.GetImportDecisions(It.IsAny<List<LocalChapter>>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<MangaImportDecision>());
        }

        // ===================== gap-06 — downloadId fast-path =====================

        [Test]
        public void GetMediaFiles_with_non_null_downloadId_AND_existing_state_row_overrides_folder_with_StagingPath()
        {
            var stateRow = Builder<ChapterDownloadState>.CreateNew()
                .With(s => s.MangaId = 42)
                .With(s => s.StagingPath = _trackedFolder)
                .Build();

            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Setup(r => r.FindByDownloadId("abc123"))
                .Returns(stateRow);

            Subject.GetMediaFiles(folder: _callerFolder, downloadId: "abc123", mangaId: null, filterExistingFiles: false);

            // Downstream consumed mangaId == 42 (sourced from stateRow): GetManga(42) called.
            Mocker.GetMock<IMangaService>()
                .Verify(s => s.GetManga(42), Times.AtLeastOnce);

            // Downstream consumed folder == _trackedFolder (sourced from stateRow.StagingPath):
            // FolderExists(_trackedFolder) called.
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.FolderExists(_trackedFolder), Times.AtLeastOnce);
        }

        [Test]
        public void GetMediaFiles_with_non_null_downloadId_AND_existing_state_row_does_NOT_override_caller_supplied_mangaId()
        {
            var stateRow = Builder<ChapterDownloadState>.CreateNew()
                .With(s => s.MangaId = 42)
                .With(s => s.StagingPath = _trackedFolder)
                .Build();

            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Setup(r => r.FindByDownloadId("abc123"))
                .Returns(stateRow);

            Subject.GetMediaFiles(folder: _callerFolder, downloadId: "abc123", mangaId: 99, filterExistingFiles: false);

            // Caller's mangaId == 99 wins over stateRow.MangaId == 42 (intentional manual-override).
            Mocker.GetMock<IMangaService>()
                .Verify(s => s.GetManga(99), Times.AtLeastOnce);
            Mocker.GetMock<IMangaService>()
                .Verify(s => s.GetManga(42), Times.Never);
        }

        [Test]
        public void GetMediaFiles_with_non_null_downloadId_AND_null_state_row_falls_back_to_folder_fallback()
        {
            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Setup(r => r.FindByDownloadId("stale123"))
                .Returns((ChapterDownloadState)null);

            Subject.GetMediaFiles(folder: _callerFolder, downloadId: "stale123", mangaId: null, filterExistingFiles: false);

            // Lookup attempted (the fast-path tried), but stateRow was null → folder unchanged.
            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Verify(r => r.FindByDownloadId("stale123"), Times.Once);

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.FolderExists(_callerFolder), Times.AtLeastOnce);
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.FolderExists(_trackedFolder), Times.Never);
        }

        [Test]
        public void GetMediaFiles_with_null_downloadId_skips_the_fast_path_entirely()
        {
            Subject.GetMediaFiles(folder: _callerFolder, downloadId: null, mangaId: null, filterExistingFiles: false);

            // Fast-path was never attempted — zero repository calls.
            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Verify(r => r.FindByDownloadId(It.IsAny<string>()), Times.Never);

            // Caller's folder consumed downstream.
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.FolderExists(_callerFolder), Times.AtLeastOnce);
        }

        [Test]
        public void GetMediaFiles_with_non_null_downloadId_AND_state_row_with_null_StagingPath_does_NOT_overwrite_folder()
        {
            var stateRow = Builder<ChapterDownloadState>.CreateNew()
                .With(s => s.MangaId = 42)
                .With(s => s.StagingPath = null)
                .Build();

            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Setup(r => r.FindByDownloadId("abc123"))
                .Returns(stateRow);

            Subject.GetMediaFiles(folder: _callerFolder, downloadId: "abc123", mangaId: null, filterExistingFiles: false);

            // StagingPath was null → fast-path seeded mangaId only; folder unchanged.
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.FolderExists(_callerFolder), Times.AtLeastOnce);
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.FolderExists(_trackedFolder), Times.Never);

            // mangaId WAS seeded from stateRow.
            Mocker.GetMock<IMangaService>()
                .Verify(s => s.GetManga(42), Times.AtLeastOnce);
        }
    }
}
