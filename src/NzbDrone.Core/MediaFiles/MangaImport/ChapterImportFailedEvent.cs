using NzbDrone.Common.Messaging;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga event per Phase 6 D-12 — see DIVERGENCE.md.
    // Role-match analog: ChapterDownloadFailedEvent (Phase 4) for download failures;
    // this event covers IMPORT-stage failures (post-archive, pre-DB-commit).
    // Phase 8 cleanup: collapse with EpisodeImportFailedEvent if/when Tv/ deletes.
    public class ChapterImportFailedEvent : IEvent
    {
        public Manga.Manga Manga { get; set; }
        public Manga.Chapter Chapter { get; set; }
        public string SourcePath { get; set; }
        public string FailureReason { get; set; }
        public DownloadClientItem DownloadClientItem { get; set; }
    }
}
