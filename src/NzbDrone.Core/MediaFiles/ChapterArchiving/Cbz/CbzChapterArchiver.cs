using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Cbz
{
    /// <summary>
    /// Phase 4 ARCHIVE-01 — CBZ archiver (FormatKey="cbz", DEFAULT per Config.OutputFormat).
    /// Streams pages directly to a FileStream-backed ZipArchive (Pitfall 4 — NEVER an in-memory
    /// buffer or 200-page chapters OOM small servers). Entries use <see cref="CompressionLevel.NoCompression"/>
    /// (D-17 — Stored, not Deflated; .NET 9+ produces true Stored per dotnet/docs#40299;
    /// images are already JPEG/PNG/WebP-compressed, double-compression wastes CPU and bloats archives).
    ///
    /// Atomic write (D-16): writes to <c>&lt;StagingDir&gt;/&lt;OutputFilename&gt;.cbz.tmp</c>, then
    /// <see cref="IDiskProvider.MoveFile"/> rename to <c>&lt;OutputFilename&gt;.cbz</c> on completion.
    /// Reader apps (Komga / Kavita / Mihon / ComicRack) never see a half-written CBZ.
    ///
    /// Page entries are 4-digit zero-padded preserving original extension (D-15) — sourced from
    /// the scratch-dir filenames written by <c>ChapterDownloadService</c> (plan 04-03) under
    /// the canonical <c>&lt;PageIndex:D4&gt;.&lt;ext&gt;</c> shape.
    /// </summary>
    public class CbzChapterArchiver : IChapterArchiver
    {
        public string FormatKey => "cbz";

        private readonly IDiskProvider _diskProvider;
        private readonly IEnumerable<IMetadataWriter> _metadataWriters;
        private readonly Logger _logger;

        public CbzChapterArchiver(IDiskProvider diskProvider, IEnumerable<IMetadataWriter> metadataWriters, Logger logger)
        {
            _diskProvider = diskProvider;
            _metadataWriters = metadataWriters;
            _logger = logger;
        }

        public async Task<string> ArchiveAsync(ChapterArchiveRequest request, CancellationToken ct)
        {
            // Ensure staging dir exists (idempotent).
            if (!_diskProvider.FolderExists(request.StagingDir))
            {
                _diskProvider.CreateFolder(request.StagingDir);
            }

            var tmpPath = Path.Combine(request.StagingDir, request.OutputFilename + ".cbz.tmp");
            var finalPath = Path.Combine(request.StagingDir, request.OutputFilename + ".cbz");

            // Defensive: a leftover .cbz.tmp from a prior killed run breaks the rename. Clear it.
            if (_diskProvider.FileExists(tmpPath))
            {
                _logger.Debug("Clearing orphan .cbz.tmp at {0}", tmpPath);
                _diskProvider.DeleteFile(tmpPath);
            }

            // D-15 — page filenames are already <PageIndex:D4>.<ext> in scratch dir.
            // Lexicographic sort = page order. Use Ordinal to be culture-stable.
            var pageFiles = _diskProvider.GetFiles(request.ScratchDir, false)
                                         .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                                         .ToList();

            if (pageFiles.Count == 0)
            {
                throw new InvalidOperationException($"Scratch dir {request.ScratchDir} contains no pages — archiver cannot proceed.");
            }

            // Pitfall 4 — FileStream NOT in-memory buffer so 200-page chapters don't OOM.
            using (var fs = _diskProvider.OpenWriteStream(tmpPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var pageFile in pageFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    var entryName = Path.GetFileName(pageFile);    // "0001.jpg"

                    // D-17 — NoCompression produces Stored entries on .NET 9+.
                    var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                    using var entryStream = entry.Open();
                    using var pageStream = _diskProvider.OpenReadStream(pageFile);
                    await pageStream.CopyToAsync(entryStream, ct).ConfigureAwait(false);
                }

                // D-14 — iterate metadata writers; CBZ ctx streams ComicInfo into the open zip.
                foreach (var writer in _metadataWriters.Where(w => w.AppliesTo(request)))
                {
                    await writer.WriteAsync(request, new CbzArchiveOutputContext(archive), ct).ConfigureAwait(false);
                }
            }

            // D-16 — atomic rename. Same-volume by construction (StagingDir is single dir).
            _diskProvider.MoveFile(tmpPath, finalPath, overwrite: true);
            _logger.Info("Archived chapter {0} → {1}", request.OutputFilename, finalPath);
            return finalPath;
        }
    }
}
