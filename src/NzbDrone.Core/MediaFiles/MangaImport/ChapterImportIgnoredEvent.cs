using NzbDrone.Common.Messaging;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga event — see DIVERGENCE.md.
    // Role-match analog: Sonarr's DownloadIgnoredEvent ("a completed download we deliberately
    // chose NOT to import"). DISTINCT from ChapterImportFailedEvent (which covers import-stage
    // EXCEPTIONS — something broke). This event covers a completed download that finished fine
    // but was intentionally not imported because the chapter is already owned or the release is
    // not an upgrade. ChapterHistoryService writes a (neutral) Ignored row for it — without this
    // the only surface was a transient queue Warn that vanished on cleanup, so the persisted
    // History kept only the original Grabbed row (debug: reimport-no-history-event).
    public class ChapterImportIgnoredEvent : IEvent
    {
        public Manga.Manga Manga { get; set; }
        public Manga.Chapter Chapter { get; set; }
        public string SourcePath { get; set; }

        // Human-readable summary surfaced as the History "Message" Data key (e.g. the
        // NotUpgradeAllowed rejection text, or "Chapter already imported").
        public string Reason { get; set; }

        // The ImportRejectionReason name(s) (e.g. "NotUpgradeAllowed", "ChapterAlreadyImported")
        // surfaced as the History "RejectionType" Data key for diagnostics.
        public string RejectionType { get; set; }

        // Indexer/SourceKey of the grabbed release (when known) — Ignored Data key per Q-4.
        public string Indexer { get; set; }

        // Release identity carried onto the history row so the Ignored row shows the same
        // Language + Scanlation Group columns as its paired Grabbed row (not blank cells).
        public string TranslatedLanguage { get; set; }
        public string ScanlationGroup { get; set; }

        public DownloadClientItem DownloadClientItem { get; set; }
    }
}
