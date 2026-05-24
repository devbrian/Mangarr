using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    using Manga = NzbDrone.Core.Manga.Manga;

    // Phase 30 Plan 30-05 (II2-03) — Manga peer of Sonarr's UpdateMediaInfoService (TV).
    //
    // D-05 LOCKED: no library-scan-event backfill daemon (see test
    // should_not_implement_ihandle_manga_scanned_event_per_D05). Probe-on-import only.
    // The single Update(ChapterFile, Manga) entry point is wired by ImportApprovedChapters
    // at step 3.5 (Pitfall 4 ordering — AFTER DB commit + filesystem move, BEFORE
    // ChapterImportedEvent publish; see Plan 30-05 Task 4).
    //
    // D-06: ImageSharp 3.1.12 is the probe library (cross-platform; already pinned at
    // Mangarr.Core.csproj:26; used by MediaCover/ImageResizer.cs).
    //
    // D-07: Probe samples first + middle + last page only (3 samples max). Color = OR over
    // sampled pages; DPI = first sampled page (single-value). Page count = enumerate
    // archive entries matching image extensions.
    //
    // D-09 non-fatal: full body wrapped in try/catch. Probe failure -> Warn log + return
    // false; MediaInfo stays null; import succeeds. Token render skips null per
    // MangaFileNameBuilder D-09.
    //
    // R-4 mitigation: DPI is accepted only when ResolutionUnits == PixelsPerInch AND
    // HorizontalResolution >= 72 AND != 96. ImageSharp returns default 96 DPI when EXIF/PNG
    // metadata is absent; this heuristic filters the false-positive.
    //
    // R-3 acknowledged: 3-sample color heuristic can flag B&W manga as Color when the
    // single sampled cover/insert page is color. Under-sampling is the safer error per D-07.
    //
    // Memory bound: each sampled page is decoded as Image<Rgba32> (~24 MB for a 2000x3000
    // page). 3 samples => max ~72 MB transient. `using` rigorously to release between pages.
    public class UpdateChapterInfoService : IUpdateChapterInfo
    {
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        private readonly IDiskProvider _diskProvider;
        private readonly IChapterFileService _chapterFileService;
        private readonly Logger _logger;

        public UpdateChapterInfoService(IDiskProvider diskProvider, IChapterFileService chapterFileService, Logger logger)
        {
            _diskProvider = diskProvider;
            _chapterFileService = chapterFileService;
            _logger = logger;
        }

        public bool Update(ChapterFile chapterFile, Manga manga)
        {
            if (chapterFile == null)
            {
                return false;
            }

            var path = chapterFile.Path.IsNotNullOrWhiteSpace()
                ? chapterFile.Path
                : (manga?.Path).IsNotNullOrWhiteSpace() && chapterFile.RelativePath.IsNotNullOrWhiteSpace()
                    ? Path.Combine(manga.Path, chapterFile.RelativePath)
                    : null;

            if (path.IsNullOrWhiteSpace() || !_diskProvider.FileExists(path))
            {
                return false;
            }

            var mediaInfo = ReadMediaInfo(path);
            if (mediaInfo == null)
            {
                return false;
            }

            chapterFile.MediaInfo = mediaInfo;
            if (chapterFile.Id != 0)
            {
                _chapterFileService.Update(chapterFile);
            }

            return true;
        }

        // D-09 non-fatal: returns null instead of throwing on any internal failure
        // (corrupt archive, unreadable image, etc.). Caller (Update) treats null as
        // "no probe data" and returns false; ImportApprovedChapters wraps the entire
        // Update call in an outer try/catch as additional defense.
        private ChapterMediaInfo ReadMediaInfo(string cbzPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(cbzPath);
                var imageEntries = archive.Entries
                    .Where(e => ImageExtensions.Contains(Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(e => e.Name, StringComparer.Ordinal)
                    .ToList();

                if (imageEntries.Count == 0)
                {
                    return null;
                }

                var indices = SampleIndices(imageEntries.Count);
                int? firstDpi = null;
                var anyColor = false;

                foreach (var idx in indices)
                {
                    var entry = imageEntries[idx];
                    try
                    {
                        using var entryStream = entry.Open();
                        using var ms = new MemoryStream();
                        entryStream.CopyTo(ms);
                        ms.Position = 0;

                        // Cheap metadata probe (no pixel decode). Used for DPI.
                        var imgInfo = Image.Identify(ms);
                        if (imgInfo != null && firstDpi == null)
                        {
                            var dpiCandidate = ExtractDpi(imgInfo.Metadata);
                            if (dpiCandidate.HasValue)
                            {
                                firstDpi = dpiCandidate;
                            }
                        }

                        // Decode pixels for color detection — only if we haven't already found color.
                        if (!anyColor)
                        {
                            ms.Position = 0;
                            using var img = Image.Load<Rgba32>(ms);
                            if (IsColorImage(img))
                            {
                                anyColor = true;
                            }
                        }
                    }
                    catch (Exception sampleEx)
                    {
                        // Per-sample failure shouldn't abort the whole probe — log + continue.
                        _logger.Warn(sampleEx, "ImageSharp probe failed for entry '{0}' in archive '{1}'; skipping sample", entry.Name, cbzPath);
                    }
                }

                return new ChapterMediaInfo
                {
                    PageCount = imageEntries.Count,
                    Color = anyColor,
                    DpiHorizontal = firstDpi
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "ImageSharp probe failed for archive '{0}'; MediaInfo left null (D-09 non-fatal)", cbzPath);
                return null;
            }
        }

        // D-07 sampling indices:
        //   1 page  => [0]
        //   2 pages => [0, 1]
        //   3+      => [0, count/2, count-1]
        private static IEnumerable<int> SampleIndices(int count)
        {
            return count switch
            {
                <= 0 => Array.Empty<int>(),
                1 => new[] { 0 },
                2 => new[] { 0, 1 },
                _ => new[] { 0, count / 2, count - 1 }
            };
        }

        // R-4 mitigation: only accept DPI when EXIF/PNG specifies a real PixelsPer{Inch,Meter}
        // unit AND the converted DPI value is in a plausible range AND not the ImageSharp
        // default-96 fallback. Returns null when the metadata is absent or unreliable.
        //
        // PNG's pHYs chunk stores units as pixels/meter (the PNG-native unit). ImageSharp
        // round-trips PixelsPerInch as PixelsPerMeter on PNG save, so we accept either unit
        // and convert PixelsPerMeter to PixelsPerInch (1 inch = 0.0254 m).
        private static int? ExtractDpi(ImageMetadata metadata)
        {
            if (metadata == null)
            {
                return null;
            }

            double dpi;
            switch (metadata.ResolutionUnits)
            {
                case PixelResolutionUnit.PixelsPerInch:
                    dpi = metadata.HorizontalResolution;
                    break;
                case PixelResolutionUnit.PixelsPerMeter:
                    // Convert px/m -> px/in (1 in = 0.0254 m).
                    dpi = metadata.HorizontalResolution * 0.0254;
                    break;
                case PixelResolutionUnit.PixelsPerCentimeter:
                    dpi = metadata.HorizontalResolution * 2.54;
                    break;
                default:
                    return null;
            }

            if (dpi < 72 || Math.Abs(dpi - 96.0) < 0.5)
            {
                return null;
            }

            return (int)Math.Round(dpi);
        }

        // Color-vs-B&W detection: sample up to 100 random pixels with a fixed seed
        // (RESEARCH §5.3 — determinism across runs). Returns true on first pixel
        // exhibiting non-grayscale chroma (channel spread > 8) — a typical
        // discriminator that ignores JPEG chroma noise in B&W scans.
        private static bool IsColorImage(Image<Rgba32> img)
        {
            var samples = Math.Min(100, img.Width * img.Height);
            var rng = new Random(42);

            for (var i = 0; i < samples; i++)
            {
                var x = rng.Next(img.Width);
                var y = rng.Next(img.Height);
                var px = img[x, y];

                if (Math.Abs(px.R - px.G) > 8 || Math.Abs(px.G - px.B) > 8 || Math.Abs(px.R - px.B) > 8)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
