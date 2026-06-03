using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 — DTO carrying everything an archiver + metadata writer needs for ONE chapter.
    /// Constructed by <c>ChapterDownloadService</c> (plan 04-03) just before invoking
    /// <see cref="IChapterArchiver.ArchiveAsync"/>.
    ///
    /// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — Manga / Chapter properties
    /// retyped from TV (Series / Episode) to manga domain types per Plan 15-03 Tv/ DELETE.
    /// </summary>
    public sealed class ChapterArchiveRequest
    {
        public NzbDrone.Core.Manga.Manga Manga { get; init; }
        public NzbDrone.Core.Manga.Chapter Chapter { get; init; }
        public ReleaseInfo Release { get; init; }

        /// <summary><c>Config.DownloadScratchPath</c>/<c>{download-id}</c>/ — pages live here. (Phase 39 RETIRE-01: the scratch sub-dir was formerly keyed by the now-retired in-process ChapterDownloadState.Id.)</summary>
        public string ScratchDir { get; init; }

        /// <summary><c>{DataDir}/completed/{mangaSlug}</c>/ — final CBZ or folder lands here.</summary>
        public string StagingDir { get; init; }

        /// <summary>Filename WITHOUT extension — archiver appends ".cbz" or "/" (folder).</summary>
        public string OutputFilename { get; init; }

        /// <summary>Total pages (informational; equal to scratch-dir file count).</summary>
        public int PageCount { get; init; }
    }
}
