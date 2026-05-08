using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Notifications
{
    // Sonarr divergence: NEW manga sibling — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Notifications/EpisodeDeleteMessage.cs (TV; deleted in Phase 15 W-1/W-2).
    // Carries the payload for INotification.OnChapterFileDelete + OnChapterFileDeleteForUpgrade.
    // Surface-only addition (no v1 publisher) — providers (v1.1+ Discord/email/webhook) opt in
    // by overriding the matching virtual no-op on NotificationBase. Mirrors the OnMangaAdd /
    // OnMangaDelete / OnMangaRename precedent shipped under Phase 8 Plan 99-08.
    public class ChapterFileDeleteMessage
    {
        public string Message { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public ChapterFile ChapterFile { get; set; }
        public DeleteMediaFileReason Reason { get; set; }

        public override string ToString()
        {
            return Message;
        }
    }
}
