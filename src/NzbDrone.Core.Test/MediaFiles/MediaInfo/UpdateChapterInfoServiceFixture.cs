using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Categories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;

namespace NzbDrone.Core.Test.MediaFiles.MediaInfo
{
    using Manga = NzbDrone.Core.Manga.Manga;

    // Phase 30 Plan 30-05 Task 3 (II2-03) — UpdateChapterInfoService probe coverage.
    // Wave 0 fixture; mirrors the CbzChapterArchiverFixture disk-mock pattern (Phase 4)
    // but generates REAL CBZ archives containing REAL ImageSharp-encoded JPG/PNG pages
    // so the probe paths (Image.Identify metadata + Image.Load color sampling) get
    // exercised against the production library.
    //
    // D-05 (no-daemon) verified via [Test]: service must NOT also implement
    // IHandle<MangaScannedEvent>.
    [TestFixture]
    [DiskAccessTest]
    public class UpdateChapterInfoServiceFixture : CoreTest<UpdateChapterInfoService>
    {
        private string _cbzDir;
        private Manga _manga;
        private ChapterFile _chapterFile;

        [SetUp]
        public void Setup()
        {
            _cbzDir = Path.Combine(TempFolder, "cbz");
            Directory.CreateDirectory(_cbzDir);

            _manga = new Manga { Id = 7, Path = _cbzDir };
            _chapterFile = new ChapterFile
            {
                Id = 0,                            // 0 => skip _chapterFileService.Update path
                MangaId = 7,
                ChapterId = 99,
                Path = null,
                RelativePath = "test.cbz"
            };

            // Mock IDiskProvider.FileExists delegate to real File.Exists.
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns<string>(File.Exists);
        }

        // ============================================================
        // Test 1 — PageCount populated from archive image-entry count.
        // ============================================================
        [Test]
        public void should_populate_page_count_from_archive_entries()
        {
            var cbz = BuildCbz("test.cbz", new[]
            {
                ("0001.png", MakeGrayscalePngBytes(width: 50, height: 50)),
                ("0002.png", MakeGrayscalePngBytes(width: 50, height: 50)),
                ("0003.png", MakeGrayscalePngBytes(width: 50, height: 50)),
                ("0004.png", MakeGrayscalePngBytes(width: 50, height: 50)),
                ("0005.png", MakeGrayscalePngBytes(width: 50, height: 50))
            });
            _chapterFile.Path = cbz;

            var ok = Subject.Update(_chapterFile, _manga);

            ok.Should().BeTrue();
            _chapterFile.MediaInfo.Should().NotBeNull();
            _chapterFile.MediaInfo.PageCount.Should().Be(5);
        }

        // ============================================================
        // Test 2 — Color page detected as Color=true.
        // ============================================================
        [Test]
        public void should_detect_color_image()
        {
            var cbz = BuildCbz("color.cbz", new[]
            {
                ("0001.png", MakeColorPngBytes(width: 100, height: 100))
            });
            _chapterFile.Path = cbz;

            var ok = Subject.Update(_chapterFile, _manga);

            ok.Should().BeTrue();
            _chapterFile.MediaInfo.Color.Should().BeTrue();
        }

        // ============================================================
        // Test 3 — B&W (grayscale) page detected as Color=false.
        // ============================================================
        [Test]
        public void should_detect_bw_image()
        {
            var cbz = BuildCbz("bw.cbz", new[]
            {
                ("0001.png", MakeGrayscalePngBytes(width: 100, height: 100))
            });
            _chapterFile.Path = cbz;

            var ok = Subject.Update(_chapterFile, _manga);

            ok.Should().BeTrue();
            _chapterFile.MediaInfo.Color.Should().BeFalse();
        }

        // ============================================================
        // Test 4 — Archive with no image entries returns false.
        // ============================================================
        [Test]
        public void should_return_false_on_archive_with_no_images()
        {
            var cbz = BuildCbz("notes.cbz", new[]
            {
                ("readme.txt", System.Text.Encoding.UTF8.GetBytes("hello"))
            });
            _chapterFile.Path = cbz;

            var ok = Subject.Update(_chapterFile, _manga);

            ok.Should().BeFalse();
            _chapterFile.MediaInfo.Should().BeNull();
        }

        // ============================================================
        // Test 5 — DPI populated when image carries EXIF/PNG metadata >= 72 and != 96.
        // ============================================================
        [Test]
        public void should_set_dpi_when_metadata_present()
        {
            var cbz = BuildCbz("dpi.cbz", new[]
            {
                ("0001.png", MakePngBytesWithDpi(width: 100, height: 100, dpi: 300))
            });
            _chapterFile.Path = cbz;

            var ok = Subject.Update(_chapterFile, _manga);

            ok.Should().BeTrue();
            _chapterFile.MediaInfo.DpiHorizontal.Should().Be(300);
        }

        // ============================================================
        // Test 6 — DPI stays null when image carries the ImageSharp default 96 (R-4).
        // ============================================================
        [Test]
        public void should_leave_dpi_null_for_default_96()
        {
            var cbz = BuildCbz("nodpi.cbz", new[]
            {
                ("0001.png", MakeGrayscalePngBytes(width: 100, height: 100))
            });
            _chapterFile.Path = cbz;

            var ok = Subject.Update(_chapterFile, _manga);

            ok.Should().BeTrue();
            _chapterFile.MediaInfo.DpiHorizontal.Should().BeNull("R-4 — ImageSharp default 96 DPI is filtered out");
        }

        // ============================================================
        // Test 7 — Corrupt archive returns false; no exception escapes (D-09 non-fatal).
        // ============================================================
        [Test]
        public void should_not_throw_on_corrupt_archive()
        {
            var cbz = Path.Combine(_cbzDir, "corrupt.cbz");
            File.WriteAllBytes(cbz, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE });
            _chapterFile.Path = cbz;

            var ok = Subject.Update(_chapterFile, _manga);

            ok.Should().BeFalse();
            _chapterFile.MediaInfo.Should().BeNull();

            // D-09 non-fatal: probe failure logs Warn + returns false (does not throw).
            // CoreTest's ExceptionVerification target asserts no unexpected Warns at TearDown,
            // so we acknowledge the expected single Warn from ReadMediaInfo's outer catch.
            ExceptionVerification.ExpectedWarns(1);
        }

        // ============================================================
        // Test 8 — Nonexistent file returns false.
        // ============================================================
        [Test]
        public void should_return_false_when_file_missing()
        {
            _chapterFile.Path = Path.Combine(_cbzDir, "missing.cbz");

            var ok = Subject.Update(_chapterFile, _manga);

            ok.Should().BeFalse();
        }

        // ============================================================
        // Test 9 — D-05 verification: service does NOT implement IHandle<MangaScannedEvent>.
        //          Type-level reflection check; prevents accidental future addition of
        //          a backfill daemon contract.
        // ============================================================
        [Test]
        public void should_not_implement_ihandle_manga_scanned_event_per_D05()
        {
            var iface = typeof(UpdateChapterInfoService)
                .GetInterfaces()
                .Select(i => i.FullName ?? string.Empty)
                .ToList();

            iface.Should().NotContain(n => n.Contains("IHandle") && n.Contains("MangaScannedEvent"),
                "D-05 LOCKED — probe-on-import only. No backfill daemon.");
        }

        // ============================================================
        // Helpers — build a CBZ on disk with the given entries.
        // ============================================================
        private string BuildCbz(string filename, (string Name, byte[] Bytes)[] entries)
        {
            var path = Path.Combine(_cbzDir, filename);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var (name, bytes) in entries)
                {
                    var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                    using var es = entry.Open();
                    es.Write(bytes, 0, bytes.Length);
                }
            }

            return path;
        }

        // Generate a grayscale PNG (all pixels R=G=B) at default 96 DPI.
        private static byte[] MakeGrayscalePngBytes(int width, int height)
        {
            using var img = new Image<Rgba32>(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var v = (byte)((x + y) & 0xFF);
                    img[x, y] = new Rgba32(v, v, v, 255);
                }
            }

            using var ms = new MemoryStream();
            img.Save(ms, new PngEncoder());
            return ms.ToArray();
        }

        // Generate a color PNG (red square) at default 96 DPI.
        private static byte[] MakeColorPngBytes(int width, int height)
        {
            using var img = new Image<Rgba32>(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    img[x, y] = new Rgba32(255, 0, 0, 255);
                }
            }

            using var ms = new MemoryStream();
            img.Save(ms, new PngEncoder());
            return ms.ToArray();
        }

        // Generate a PNG with explicit DPI metadata set to `dpi` PixelsPerInch.
        private static byte[] MakePngBytesWithDpi(int width, int height, int dpi)
        {
            using var img = new Image<Rgba32>(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    img[x, y] = new Rgba32(128, 128, 128, 255);
                }
            }

            img.Metadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
            img.Metadata.HorizontalResolution = dpi;
            img.Metadata.VerticalResolution = dpi;

            using var ms = new MemoryStream();
            img.Save(ms, new PngEncoder());
            return ms.ToArray();
        }
    }
}
