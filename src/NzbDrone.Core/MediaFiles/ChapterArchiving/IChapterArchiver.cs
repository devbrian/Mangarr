using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 D-13 — pluggable archive-format strategy.
    /// v1 implementations: <c>CbzChapterArchiver</c> (FormatKey="cbz", default) +
    /// <c>FolderImagesChapterArchiver</c> (FormatKey="folder").
    /// v2 may add CBR / EPUB-comic / raw-image-dump impls — zero edits to downloader/queue/import.
    /// ARCHIVE-05 plugin-contract honored literally.
    ///
    /// Phase 8 cleanup: stays as-is (manga-shaped from Day 1; no TV peer to collapse with).
    /// </summary>
    public interface IChapterArchiver
    {
        /// <summary>Stable identifier matched against <c>Config.OutputFormat</c>.</summary>
        string FormatKey { get; }

        /// <summary>
        /// Reads pages from <c>request.ScratchDir</c>, packages per format, writes to
        /// <c>request.StagingDir</c> via .tmp+atomic-rename (D-16). Returns the final on-disk
        /// path (Phase 4 D-11 — surfaced on <c>DownloadClientItem.OutputPath</c>).
        /// </summary>
        Task<string> ArchiveAsync(ChapterArchiveRequest request, CancellationToken ct);
    }
}
