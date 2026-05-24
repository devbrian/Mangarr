using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Metadata;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Folder
{
    /// <summary>
    /// Phase 4 ARCHIVE-02 — folder-of-images archiver (FormatKey="folder").
    /// Produces a directory of zero-padded numbered images:
    ///   &lt;Manga&gt; - Chapter 132/0001.jpg, 0002.png, …, ComicInfo.xml
    /// Some readers (Komga, Kavita) prefer this format for faster random-access.
    ///
    /// Atomic directory rename (D-16): writes images to <c>&lt;StagingDir&gt;/&lt;OutputFilename&gt;.tmp/</c>,
    /// then <see cref="IDiskProvider.MoveFolder"/> rename to <c>&lt;OutputFilename&gt;/</c> on completion.
    /// Reader apps (Komga / Kavita / Mihon / ComicRack) never see a half-written folder.
    ///
    /// Page filenames preserve the 4-digit zero-padded shape from the scratch dir (D-15).
    /// File contents are pass-through bytes (no transcoding — D-17 by analogy: image bytes
    /// from the CDN are already JPEG/PNG/WebP-compressed).
    ///
    /// Sibling shape to <c>CbzChapterArchiver</c>: same DI graph, same atomic-tmp-rename pattern,
    /// same metadata-writer iteration. Differences:
    /// - <see cref="IDiskProvider.MoveFolder"/> instead of <see cref="IDiskProvider.MoveFile"/>
    /// - <see cref="FolderArchiveOutputContext"/> instead of <c>CbzArchiveOutputContext</c>
    /// - No compression decision (folder mode = pass-through bytes; ZIP doesn't enter the picture).
    ///
    /// Phase 30 Plan 30-04 D-02 flip — metadata writer enumeration changed from the
    /// DryIoc auto-discovered legacy metadata-writer enumeration to
    /// <see cref="IMetadataFactory.Enabled"/>. See <c>CbzChapterArchiver</c> header for full
    /// flip rationale.
    /// </summary>
    public class FolderImagesChapterArchiver : IChapterArchiver
    {
        public string FormatKey => "folder";

        private readonly IDiskProvider _diskProvider;
        private readonly IMetadataFactory _metadataFactory;
        private readonly Logger _logger;

        public FolderImagesChapterArchiver(IDiskProvider diskProvider, IMetadataFactory metadataFactory, Logger logger)
        {
            _diskProvider = diskProvider;
            _metadataFactory = metadataFactory;
            _logger = logger;
        }

        public async Task<string> ArchiveAsync(ChapterArchiveRequest request, CancellationToken ct)
        {
            // Ensure staging dir exists (idempotent).
            if (!_diskProvider.FolderExists(request.StagingDir))
            {
                _diskProvider.CreateFolder(request.StagingDir);
            }

            var tmpDir   = Path.Combine(request.StagingDir, request.OutputFilename + ".tmp");
            var finalDir = Path.Combine(request.StagingDir, request.OutputFilename);

            // Defensive: a leftover .tmp dir from a prior killed run breaks rename. Clear it.
            if (_diskProvider.FolderExists(tmpDir))
            {
                _logger.Debug("Clearing orphan .tmp folder at {0}", tmpDir);
                _diskProvider.DeleteFolder(tmpDir, true);
            }

            _diskProvider.CreateFolder(tmpDir);

            // D-15 — page filenames are <PageIndex:D4>.<ext> in scratch dir.
            // Lexicographic sort = page order. Use Ordinal to be culture-stable.
            var pageFiles = _diskProvider.GetFiles(request.ScratchDir, false)
                                         .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                                         .ToList();

            if (pageFiles.Count == 0)
            {
                _diskProvider.DeleteFolder(tmpDir, true);
                throw new InvalidOperationException($"Scratch dir {request.ScratchDir} contains no pages — archiver cannot proceed.");
            }

            try
            {
                foreach (var pageFile in pageFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    var dest = Path.Combine(tmpDir, Path.GetFileName(pageFile));
                    _diskProvider.CopyFile(pageFile, dest);
                }

                // D-14 + Phase 30 D-02 — iterate metadata providers via the ThingiProvider
                // factory; folder writer drops sibling files into tmpDir BEFORE the rename
                // so they land inside the final dir atomically.
                foreach (var writer in _metadataFactory.Enabled().Where(w => w.AppliesTo(request)))
                {
                    ct.ThrowIfCancellationRequested();
                    await writer.WriteAsync(request, new FolderArchiveOutputContext(tmpDir, _diskProvider), ct).ConfigureAwait(false);
                }
            }
            catch
            {
                // T-04-15 mitigation — never leave a half-written .tmp dir to be observed.
                if (_diskProvider.FolderExists(tmpDir))
                {
                    _diskProvider.DeleteFolder(tmpDir, true);
                }

                throw;
            }

            // D-16 — atomic dir rename. Same-volume by construction (StagingDir is single dir).
            _diskProvider.MoveFolder(tmpDir, finalDir);
            _logger.Info("Archived chapter {0} to folder {1}", request.OutputFilename, finalDir);
            return finalDir;
        }
    }
}
