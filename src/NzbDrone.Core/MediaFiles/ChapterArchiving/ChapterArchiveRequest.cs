using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 — DTO carrying everything an archiver + metadata writer needs for ONE chapter.
    /// Constructed by <c>ChapterDownloadService</c> (plan 04-03) just before invoking
    /// <see cref="IChapterArchiver.ArchiveAsync"/>. Phase 8 collapse: <c>Manga</c>+<c>Chapter</c>
    /// fields type-rename when the domain rename runs; structure stays.
    /// </summary>
    public sealed class ChapterArchiveRequest
    {
        // Phase 8 will rename Series → Manga / Episode → Chapter. Phase 4 uses the existing
        // TV-shaped property types because the rename is centralized at Phase 8.
        public Series Manga { get; init; }
        public Episode Chapter { get; init; }
        public ReleaseInfo Release { get; init; }

        /// <summary><c>Config.DownloadScratchPath</c>/<c>{ChapterDownloadState.Id}</c>/ — pages live here.</summary>
        public string ScratchDir { get; init; }

        /// <summary><c>{DataDir}/completed/{mangaSlug}</c>/ — final CBZ or folder lands here.</summary>
        public string StagingDir { get; init; }

        /// <summary>Filename WITHOUT extension — archiver appends ".cbz" or "/" (folder).</summary>
        public string OutputFilename { get; init; }

        /// <summary>Total pages (informational; equal to scratch-dir file count).</summary>
        public int PageCount { get; init; }
    }
}
