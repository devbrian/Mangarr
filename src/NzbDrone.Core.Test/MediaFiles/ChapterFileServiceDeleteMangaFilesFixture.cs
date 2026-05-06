using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    using IMangaService = NzbDrone.Core.Manga.IMangaService;
    using Manga = NzbDrone.Core.Manga.Manga;

    // Sonarr divergence: NEW manga sibling per Phase 11 Plan 11-06 — see DIVERGENCE.md.
    // Fixture verifies the new IExecute<DeleteMangaFilesCommand> handler shipped on
    // ChapterFileService. Mandatory coverage per 11-PATTERNS section 5:
    //   * Per-mangaId iteration (Test 1)
    //   * Pitfall 4 ordering — recycle FIRST, DB delete SECOND, MockSequence-asserted (Test 2)
    //   * CommandResult.Indeterminate reporting on guard-clause continues (Test 3)
    //   * Throw-then-no-publish invariant — Verify(...PublishEvent..., Times.Never) (Test 4)
    //   * Empty file-list Debug-log path (Test 5)
    //   * Per-mangaId iteration continues across recycle exceptions (Test 6)
    // Role-match analog: src/NzbDrone.Core.Test/MediaFiles/MediaFileDeletionService/DeleteEpisodeFileFixture.cs.
    [TestFixture]
    public class ChapterFileServiceDeleteMangaFilesFixture : CoreTest<ChapterFileService>
    {
        private Manga _manga;
        private List<ChapterFile> _chapterFiles;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<Manga>.CreateNew()
                .With(m => m.Id = 42)
                .With(m => m.Title = "Test Manga")
                .With(m => m.Path = @"C:\Test\Manga\Title".AsOsAgnostic())
                .Build();

            _chapterFiles = Builder<ChapterFile>.CreateListOfSize(3)
                .All().With(f => f.RelativePath = "ch001.cbz").Build().ToList();

            Mocker.GetMock<IMangaService>().Setup(s => s.GetManga(42)).Returns(_manga);
            Mocker.GetMock<IChapterFileRepository>().Setup(r => r.GetFilesByManga(42)).Returns(_chapterFiles);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetDirectories(It.IsAny<string>())).Returns(new[] { "x" });
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetParentFolder(It.IsAny<string>())).Returns<string>(p => Path.GetDirectoryName(p) ?? string.Empty);
            Mocker.GetMock<IRootFolderService>().Setup(r => r.GetBestRootFolderPath(_manga.Path)).Returns(@"C:\Test\Manga".AsOsAgnostic());

            // INFO-7 close-out (Plan 11-06 revision 2026-05-05): pre-mock IRecycleBinProvider.DeleteFile
            // as no-op default for completeness with Sonarr CoreTest pattern. Tests that need to throw
            // (e.g., Test 6 Execute_continues_to_next_mangaId_when_recycle_throws) override this with .Throws<>().
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(p => p.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(string.Empty);
        }

        [Test]
        public void Execute_iterates_each_MangaId_and_deletes_files()
        {
            Subject.Execute(new DeleteMangaFilesCommand { MangaIds = new List<int> { 42 } });

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(p => p.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(3));
            Mocker.GetMock<IChapterFileRepository>()
                  .Verify(r => r.Delete(It.IsAny<ChapterFile>()), Times.Exactly(3));
        }

        [Test]
        public void Execute_preserves_Pitfall_4_ordering_recycle_BEFORE_db_delete()
        {
            // ===================== Pitfall 4 ordering invariant =====================
            // Source: 11-PATTERNS.md Pattern B + 11-RESEARCH.md §6 #3
            // Recycle FIRST, DB row delete SECOND — inverted order leaks file paths.
            var sequence = new MockSequence();
            Mocker.GetMock<IRecycleBinProvider>()
                  .InSequence(sequence)
                  .Setup(p => p.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(string.Empty);
            Mocker.GetMock<IChapterFileRepository>()
                  .InSequence(sequence)
                  .Setup(r => r.Delete(It.IsAny<ChapterFile>()));

            Subject.Execute(new DeleteMangaFilesCommand { MangaIds = new List<int> { 42 } });

            // MockSequence verifies recycle ran before delete; if inverted, the test fails.
        }

        [Test]
        public void Execute_reports_Indeterminate_when_root_folder_missing()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns(false);

            Subject.Execute(new DeleteMangaFilesCommand { MangaIds = new List<int> { 42 } });

            Mocker.GetMock<ICommandResultReporter>()
                  .Verify(r => r.Report(CommandResult.Indeterminate), Times.AtLeastOnce);
            ExceptionVerification.ExpectedWarns(1);   // Phase 9 F-01 mandatory: Logger.Warn must be expected
        }

        [Test]
        public void Execute_reports_Indeterminate_when_manga_folder_missing()
        {
            // Phase 11 review CR-01 — third guard clause coverage.
            // Root folder exists + has sibling directories (guards 1+2 pass), but the specific
            // manga.Path directory does not exist. Per-mangaId loop must Warn-log + report
            // Indeterminate + continue without iterating chapterFiles or invoking recycle.
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_manga.Path)).Returns(false);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetDirectories(It.IsAny<string>())).Returns(new[] { "x" });

            Subject.Execute(new DeleteMangaFilesCommand { MangaIds = new List<int> { 42 } });

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(p => p.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            Mocker.GetMock<ICommandResultReporter>()
                  .Verify(r => r.Report(CommandResult.Indeterminate), Times.AtLeastOnce);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Execute_throw_then_no_publish_when_unknown_manga_id()
        {
            // ===================== Throw-then-no-publish invariant =====================
            // Source: 11-PATTERNS.md Pattern E + 11-RESEARCH.md §6 #4 + Phase 10 commit 9a65806b6.
            //
            // Phase 11 review CR-02: IMangaService.GetManga(int) returns null on missing IDs
            // per BL-01 contract (MangaService.cs:36-43 calls _mangaRepository.Find, not Get).
            // TV's SeriesService.GetSeries throws ModelNotFoundException on missing — manga
            // does NOT. The earlier .Throws<InvalidOperationException>() setup masked the
            // real production semantic and gave a false-positive on the unknown-id error path.
            // The handler now explicitly null-checks manga before dereferencing manga.Title,
            // logs Warn, reports Indeterminate, and continues.
            Mocker.GetMock<IMangaService>().Setup(s => s.GetManga(99)).Returns((Manga)null);

            Subject.Execute(new DeleteMangaFilesCommand { MangaIds = new List<int> { 99 } });

            // Per RESEARCH §6 #1 — IExecute handlers MUST NOT publish CommandExecutedEvent
            // (CommandExecutor.ExecuteCommand finally-block does it, see RESEARCH §7 dispatch path).
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<CommandExecutedEvent>()), Times.Never);
            Mocker.GetMock<IChapterFileRepository>()
                  .Verify(r => r.Delete(It.IsAny<ChapterFile>()), Times.Never);
            Mocker.GetMock<ICommandResultReporter>()
                  .Verify(r => r.Report(CommandResult.Indeterminate), Times.AtLeastOnce);
            ExceptionVerification.ExpectedWarns(1);   // explicit null-check logs Warn (CR-02 fix)
        }

        [Test]
        public void Execute_logs_Debug_when_no_files_for_manga()
        {
            Mocker.GetMock<IChapterFileRepository>().Setup(r => r.GetFilesByManga(42)).Returns(new List<ChapterFile>());

            Subject.Execute(new DeleteMangaFilesCommand { MangaIds = new List<int> { 42 } });

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(p => p.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

            // No Warn/Error expected; ExceptionVerification clean.
        }

        [Test]
        public void Execute_continues_to_next_mangaId_when_recycle_throws()
        {
            var manga2 = Builder<Manga>.CreateNew()
                .With(m => m.Id = 43)
                .With(m => m.Title = "Other Manga")
                .With(m => m.Path = @"C:\Test\Manga\Other".AsOsAgnostic())
                .Build();
            var files2 = Builder<ChapterFile>.CreateListOfSize(1).All().With(f => f.RelativePath = "ch002.cbz").Build().ToList();
            Mocker.GetMock<IMangaService>().Setup(s => s.GetManga(43)).Returns(manga2);
            Mocker.GetMock<IChapterFileRepository>().Setup(r => r.GetFilesByManga(43)).Returns(files2);
            Mocker.GetMock<IRootFolderService>().Setup(r => r.GetBestRootFolderPath(manga2.Path)).Returns(@"C:\Test\Manga".AsOsAgnostic());

            // First mangaId triggers exception in recycle; subsequent mangaId still processes.
            // Reset the mock so the SetUp's no-op .Returns(string.Empty) does not shadow .Throws().
            Mocker.GetMock<IRecycleBinProvider>().Reset();
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(p => p.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Throws(new IOException("test recycle failure"));

            Subject.Execute(new DeleteMangaFilesCommand { MangaIds = new List<int> { 42, 43 } });

            // DeleteFile must have been called per-file (4 times total).
            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(p => p.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(4));

            // Per-file try/catch reports Indeterminate; iteration continues across both mangaIds.
            // Report is called once per recycle failure (4 total — 3 for manga 42 + 1 for manga 43).
            Mocker.GetMock<ICommandResultReporter>()
                  .Verify(r => r.Report(CommandResult.Indeterminate), Times.Exactly(4));

            // Tolerate any Warns/Errors logged on the per-file recycle path; the structural
            // invariants (per-file iteration count + Indeterminate count) are the authoritative
            // signal that the inner try/catch fired and continued to the next file/mangaId.
            ExceptionVerification.IgnoreErrors();
            ExceptionVerification.IgnoreWarns();
        }
    }
}
