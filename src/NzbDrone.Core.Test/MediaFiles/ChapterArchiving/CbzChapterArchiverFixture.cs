using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Cbz;
using NzbDrone.Core.Metadata;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.Categories;

namespace NzbDrone.Core.Test.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 ARCHIVE-01 — Wave 0 fixture for <see cref="CbzChapterArchiver"/>.
    /// Exercises the real archiver against real disk; <c>IDiskProvider</c> is mocked but
    /// methods delegate to real <c>System.IO.File</c> calls. This is the Phase 3 LEARNINGS
    /// pattern — tests that exercise the actual production path catch F-01 class regressions
    /// (F-01: "DI-injecting a service is not the same as wiring its consumers").
    /// </summary>
    [TestFixture]
    [DiskAccessTest]
    public class CbzChapterArchiverFixture : CoreTest<CbzChapterArchiver>
    {
        private string _scratchDir;
        private string _stagingDir;

        [SetUp]
        public void Setup()
        {
            _scratchDir = Path.Combine(TempFolder, "scratch");
            _stagingDir = Path.Combine(TempFolder, "staging");
            Directory.CreateDirectory(_scratchDir);
            Directory.CreateDirectory(_stagingDir);

            // Seed 5 fake page files: 0001.jpg, 0002.png, 0003.jpg, 0004.webp, 0005.jpg.
            // Magic-byte sequences are plausible image headers without bundling real images.
            File.WriteAllBytes(Path.Combine(_scratchDir, "0001.jpg"), JpegMagic());
            File.WriteAllBytes(Path.Combine(_scratchDir, "0002.png"), PngMagic());
            File.WriteAllBytes(Path.Combine(_scratchDir, "0003.jpg"), JpegMagic());
            File.WriteAllBytes(Path.Combine(_scratchDir, "0004.webp"), WebpMagic());
            File.WriteAllBytes(Path.Combine(_scratchDir, "0005.jpg"), JpegMagic());

            WireDiskProvider();

            // Default: no metadata providers (ComicInfoMetadataWriter ships in plan 04-07;
            // Phase 30 Plan 30-04 Task 5 flipped the iteration to IMetadataFactory.Enabled()).
            // Each test sets this up explicitly to override (see e.g.
            // Iterates_metadata_writers_inside_archive_scope below).
            Mocker.GetMock<IMetadataFactory>()
                  .Setup(f => f.Enabled())
                  .Returns(new List<IMetadata>());
        }

        private void WireDiskProvider()
        {
            var disk = Mocker.GetMock<IDiskProvider>();

            disk.Setup(s => s.FolderExists(It.IsAny<string>()))
                .Returns<string>(Directory.Exists);

            disk.Setup(s => s.CreateFolder(It.IsAny<string>()))
                .Callback<string>(p => Directory.CreateDirectory(p));

            disk.Setup(s => s.FileExists(It.IsAny<string>()))
                .Returns<string>(File.Exists);

            disk.Setup(s => s.DeleteFile(It.IsAny<string>()))
                .Callback<string>(File.Delete);

            disk.Setup(s => s.GetFiles(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns<string, bool>((p, recursive) =>
                    Directory.GetFiles(p, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));

            disk.Setup(s => s.OpenWriteStream(It.IsAny<string>()))
                .Returns<string>(p => new FileStream(p, FileMode.Create, FileAccess.Write, FileShare.None));

            disk.Setup(s => s.OpenReadStream(It.IsAny<string>()))
                .Returns<string>(p => new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read));

            disk.Setup(s => s.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((src, dest, overwrite) =>
                {
                    if (overwrite && File.Exists(dest))
                    {
                        File.Delete(dest);
                    }

                    File.Move(src, dest);
                });
        }

        [Test]
        public async Task Produces_cbz_with_entries_in_lexicographic_order()
        {
            var req = MakeRequest(_scratchDir, _stagingDir, "Chapter 132");
            var finalPath = await Subject.ArchiveAsync(req, CancellationToken.None);

            File.Exists(finalPath).Should().BeTrue();
            finalPath.Should().EndWith(".cbz");

            using var fs = File.OpenRead(finalPath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            var names = archive.Entries.Select(e => e.Name).ToList();
            names.Should().BeInAscendingOrder();
            names.Should().BeEquivalentTo(new[] { "0001.jpg", "0002.png", "0003.jpg", "0004.webp", "0005.jpg" });
        }

        [Test]
        public async Task All_entries_use_NoCompression_Stored_method()
        {
            var req = MakeRequest(_scratchDir, _stagingDir, "Chapter 132");
            var finalPath = await Subject.ArchiveAsync(req, CancellationToken.None);

            using var fs = File.OpenRead(finalPath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                // ZipArchiveEntry exposes CompressedLength + Length; for Stored, they're equal.
                entry.CompressedLength.Should().Be(entry.Length, $"entry '{entry.Name}' should be Stored (NoCompression) per D-17 / .NET 9+ behavior");
            }
        }

        [Test]
        public async Task Atomic_rename_removes_tmp_file_on_success()
        {
            var req = MakeRequest(_scratchDir, _stagingDir, "Chapter 132");
            var finalPath = await Subject.ArchiveAsync(req, CancellationToken.None);

            File.Exists(finalPath).Should().BeTrue();
            File.Exists(Path.Combine(_stagingDir, "Chapter 132.cbz.tmp")).Should().BeFalse();
        }

        [Test]
        public async Task Empty_scratch_dir_throws_InvalidOperationException()
        {
            var emptyScratch = Path.Combine(TempFolder, "empty-scratch");
            Directory.CreateDirectory(emptyScratch);

            var req = MakeRequest(emptyScratch, _stagingDir, "Empty");
            Func<Task> act = () => Subject.ArchiveAsync(req, CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Test]
        public async Task Iterates_metadata_writers_inside_archive_scope()
        {
            // Phase 30 Plan 30-04 Task 5 flip — archivers iterate IMetadataFactory.Enabled()
            // returning List<IMetadata> instead of IEnumerable<IMetadataWriter>.
            var writer = new Mock<IMetadata>();
            writer.Setup(w => w.AppliesTo(It.IsAny<ChapterArchiveRequest>())).Returns(true);
            writer.Setup(w => w.WriteAsync(It.IsAny<ChapterArchiveRequest>(), It.IsAny<ArchiveOutputContext>(), It.IsAny<CancellationToken>()))
                  .Returns(async (ChapterArchiveRequest r, ArchiveOutputContext ctx, CancellationToken ct) =>
                  {
                      using var s = ctx.OpenSidecar("ComicInfo.xml");
                      var bytes = Encoding.UTF8.GetBytes("<ComicInfo/>");
                      await s.WriteAsync(bytes.AsMemory(), ct);
                  });

            Mocker.GetMock<IMetadataFactory>()
                  .Setup(f => f.Enabled())
                  .Returns(new List<IMetadata> { writer.Object });

            var req = MakeRequest(_scratchDir, _stagingDir, "Chapter 132");
            var finalPath = await Subject.ArchiveAsync(req, CancellationToken.None);

            using var fs = File.OpenRead(finalPath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            archive.GetEntry("ComicInfo.xml").Should().NotBeNull();
        }

        [Test]
        public async Task Cancelled_token_aborts_archive_and_does_not_create_final()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var req = MakeRequest(_scratchDir, _stagingDir, "Chapter 132");
            Func<Task> act = () => Subject.ArchiveAsync(req, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
            File.Exists(Path.Combine(_stagingDir, "Chapter 132.cbz")).Should().BeFalse();
        }

        [Test]
        public async Task Golden_fixture_entry_list_matches()
        {
            var req = MakeRequest(_scratchDir, _stagingDir, "Chapter132");
            var finalPath = await Subject.ArchiveAsync(req, CancellationToken.None);

            var goldenPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Files",
                "Phase04",
                "golden-cbz",
                "Chapter132.cbz");

            File.Exists(goldenPath).Should().BeTrue($"golden fixture must exist at {goldenPath}");

            using var actual = ZipFile.OpenRead(finalPath);
            using var golden = ZipFile.OpenRead(goldenPath);

            // Asserting entry-list shape (names + size + compression method) — NOT byte-equal
            // because ZipArchive timestamps drift across runs.
            var actualEntries = actual.Entries
                                      .Select(e => new { e.Name, e.Length, e.CompressedLength })
                                      .OrderBy(e => e.Name)
                                      .ToList();
            var goldenEntries = golden.Entries
                                      .Select(e => new { e.Name, e.Length, e.CompressedLength })
                                      .OrderBy(e => e.Name)
                                      .ToList();
            actualEntries.Should().BeEquivalentTo(goldenEntries);
        }

        private ChapterArchiveRequest MakeRequest(string scratch, string staging, string filename) =>
            new ChapterArchiveRequest
            {
                // Manga + Chapter null in this fixture — CbzChapterArchiver doesn't read them
                // (only metadata writers do; that path is exercised in plan 04-07's fixture).
                ScratchDir = scratch,
                StagingDir = staging,
                OutputFilename = filename,
                PageCount = Directory.EnumerateFiles(scratch).Count()
            };

        // Tiny magic byte sequences for fake images — enough to be plausible JPEG/PNG/WebP
        // headers without bundling real images in tests.
        private static byte[] JpegMagic() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0 };
        private static byte[] PngMagic() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static byte[] WebpMagic() => new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 };
    }
}
