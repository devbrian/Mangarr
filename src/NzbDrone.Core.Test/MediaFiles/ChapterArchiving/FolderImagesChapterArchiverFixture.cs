using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Folder;
using NzbDrone.Core.Metadata;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.Categories;

namespace NzbDrone.Core.Test.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 ARCHIVE-02 — Wave 0 fixture for <see cref="FolderImagesChapterArchiver"/>.
    /// Sibling fixture to <c>CbzChapterArchiverFixture</c> (plan 04-04 Task 2). Same wiring
    /// pattern: <c>IDiskProvider</c> is mocked but methods delegate to real <c>System.IO</c>
    /// calls so we exercise the production path against real disk. Catches F-01 class
    /// regressions ("DI-injecting a service is not the same as wiring its consumers" —
    /// Phase 3 LEARNINGS).
    /// </summary>
    [TestFixture]
    [DiskAccessTest]
    public class FolderImagesChapterArchiverFixture : CoreTest<FolderImagesChapterArchiver>
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

            // Seed 3 fake page files: 0001.jpg, 0002.png, 0003.webp.
            // Magic-byte sequences are plausible image headers without bundling real images.
            File.WriteAllBytes(Path.Combine(_scratchDir, "0001.jpg"), JpegMagic());
            File.WriteAllBytes(Path.Combine(_scratchDir, "0002.png"), PngMagic());
            File.WriteAllBytes(Path.Combine(_scratchDir, "0003.webp"), WebpMagic());

            WireDiskProvider();

            // Default: no metadata providers (ComicInfoMetadataWriter ships in plan 04-07;
            // Phase 30 Plan 30-04 Task 5 flipped the iteration to IMetadataFactory.Enabled()).
            // The Iterates_metadata_writers_into_tmp_before_rename test overrides this setup.
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

            disk.Setup(s => s.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, bool>((p, recursive) =>
                {
                    if (Directory.Exists(p))
                    {
                        Directory.Delete(p, recursive);
                    }
                });

            disk.Setup(s => s.GetFiles(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns<string, bool>((p, recursive) =>
                    Directory.GetFiles(p, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));

            disk.Setup(s => s.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((src, dest, overwrite) => File.Copy(src, dest, overwrite));

            disk.Setup(s => s.OpenWriteStream(It.IsAny<string>()))
                .Returns<string>(p => new FileStream(p, FileMode.Create, FileAccess.Write, FileShare.None));

            disk.Setup(s => s.MoveFolder(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>(Directory.Move);
        }

        [Test]
        public async Task Produces_folder_with_images_in_lex_order()
        {
            var req = MakeRequest(_scratchDir, _stagingDir, "Chapter 132");
            var finalDir = await Subject.ArchiveAsync(req, CancellationToken.None);

            Directory.Exists(finalDir).Should().BeTrue();
            var files = Directory.GetFiles(finalDir)
                                 .Select(Path.GetFileName)
                                 .OrderBy(x => x, StringComparer.Ordinal)
                                 .ToList();
            files.Should().BeEquivalentTo(new[] { "0001.jpg", "0002.png", "0003.webp" });
        }

        [Test]
        public async Task Atomic_rename_removes_tmp_dir_on_success()
        {
            var req = MakeRequest(_scratchDir, _stagingDir, "Chapter 132");
            var finalDir = await Subject.ArchiveAsync(req, CancellationToken.None);

            Directory.Exists(finalDir).Should().BeTrue();
            Directory.Exists(Path.Combine(_stagingDir, "Chapter 132.tmp")).Should().BeFalse();
        }

        [Test]
        public async Task Empty_scratch_dir_throws_and_cleans_tmp()
        {
            var emptyScratch = Path.Combine(TempFolder, "empty-scratch");
            Directory.CreateDirectory(emptyScratch);

            var req = MakeRequest(emptyScratch, _stagingDir, "Empty");
            Func<Task> act = () => Subject.ArchiveAsync(req, CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
            Directory.Exists(Path.Combine(_stagingDir, "Empty.tmp")).Should().BeFalse();
        }

        [Test]
        public async Task Iterates_metadata_writers_into_tmp_before_rename()
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
            var finalDir = await Subject.ArchiveAsync(req, CancellationToken.None);

            File.Exists(Path.Combine(finalDir, "ComicInfo.xml")).Should().BeTrue();
        }

        [Test]
        public async Task Cancelled_token_aborts_and_final_dir_absent()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var req = MakeRequest(_scratchDir, _stagingDir, "Chapter 132");
            Func<Task> act = () => Subject.ArchiveAsync(req, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
            Directory.Exists(Path.Combine(_stagingDir, "Chapter 132")).Should().BeFalse();
        }

        [Test]
        public async Task Image_byte_content_preserved()
        {
            var req = MakeRequest(_scratchDir, _stagingDir, "Chapter 132");
            var finalDir = await Subject.ArchiveAsync(req, CancellationToken.None);

            // D-17 by analogy — folder archiver is pass-through; bytes match the originals.
            var copiedJpg = File.ReadAllBytes(Path.Combine(finalDir, "0001.jpg"));
            copiedJpg.Should().BeEquivalentTo(JpegMagic());
        }

        private ChapterArchiveRequest MakeRequest(string scratch, string staging, string filename) =>
            new ChapterArchiveRequest
            {
                // Manga + Chapter null in this fixture — FolderImagesChapterArchiver doesn't read them
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
