using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 — DTO carrying everything a metadata writer needs for ONE chapter. Originally
    /// constructed by the in-process <c>ChapterDownloadService</c> just before invoking the
    /// chapter archiver (both retired in Phase 39 Plan 02, RETIRE-01). The surviving consumer
    /// is the Phase-38 <c>ComicInfoCbzInjector</c>, which reconstructs a minimal request to feed
    /// <c>ComicInfoXmlBuilder</c> when upserting ComicInfo.xml into a finished gateway CBZ.
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
