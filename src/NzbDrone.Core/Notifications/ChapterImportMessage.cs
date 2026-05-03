using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Notifications
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-18 + Pitfall 7 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Notifications/DownloadMessage.cs (TV ImportComplete shape).
    // Phase 8 cleanup: collapse with DownloadMessage when Tv/ deletes.
    public class ChapterImportMessage
    {
        public string Message { get; set; }
        public Manga.Manga Manga { get; set; }
        public Manga.Chapter Chapter { get; set; }
        public ChapterFile ChapterFile { get; set; }
        public string SourceTitle { get; set; }
        public string SourcePath { get; set; }
        public string DownloadClient { get; set; }
        public string DownloadId { get; set; }
        public bool OldFiles { get; set; }

        public override string ToString()
        {
            return Message;
        }
    }
}
