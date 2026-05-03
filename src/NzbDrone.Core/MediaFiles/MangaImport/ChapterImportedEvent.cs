using NzbDrone.Common.Messaging;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga event per Phase 6 D-18 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/Events/EpisodeImportedEvent.cs.
    // Pitfall 4 GUARD: this event MUST be published AFTER ChapterFile DB commit + filesystem
    // move complete. Plan 06-07 ImportApprovedChapters enforces; do NOT publish from inside
    // the file-move method.
    // Phase 8 cleanup: collapse with EpisodeImportedEvent when Tv/ deletes.
    public class ChapterImportedEvent : IEvent
    {
        public Manga.Manga Manga { get; set; }
        public Manga.Chapter Chapter { get; set; }
        public ChapterFile ChapterFile { get; set; }
        public DownloadClientItem DownloadClientItem { get; set; }
        public bool NewDownload { get; set; }
        public string SourcePath { get; set; }
    }
}
