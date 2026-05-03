using System;
using System.Collections.Generic;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.Queue.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-20 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Queue/Queue.cs.
    // Phase 8 cleanup: collapse with Queue when Tv/ deletes.
    //
    // Mirrors Queue's shape (TV episode counterpart) but reads from RemoteChapter
    // (Phase 5) instead of RemoteEpisode. Drops TV-only Languages / QualityModel
    // (manga has no quality model per Phase 5 D-04). Adds TranslatedLanguage +
    // ScanlationGroup as first-class fields (Phase 3 D-Q4 wire-level shape).
    //
    // Id is computed via HashConverter.GetHashInt31 over a deterministic key —
    // same TrackedDownload + chapter combination yields the same Id across refreshes
    // so SignalR clients can diff on it (Plan 09 V5 controller fan-out consumer).
    public class MangaQueueItem
    {
        public int Id { get; set; }
        public int? MangaId { get; set; }
        public int? ChapterId { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public NzbDrone.Core.Manga.Chapter Chapter { get; set; }
        public List<NzbDrone.Core.Manga.Chapter> Chapters { get; set; } = new();
        public RemoteChapter RemoteChapter { get; set; }
        public string TranslatedLanguage { get; set; }
        public string ScanlationGroup { get; set; }
        public long Size { get; set; }
        public string Title { get; set; }
        public decimal SizeLeft { get; set; }
        public TimeSpan? TimeLeft { get; set; }
        public DateTime? EstimatedCompletionTime { get; set; }
        public DateTime? Added { get; set; }
        public string Status { get; set; }
        public string TrackedDownloadStatus { get; set; }
        public string TrackedDownloadState { get; set; }
        public List<TrackedDownloadStatusMessage> StatusMessages { get; set; } = new();
        public string ErrorMessage { get; set; }
        public string DownloadId { get; set; }
        public string Indexer { get; set; }
        public string OutputPath { get; set; }
        public DownloadProtocol Protocol { get; set; }
        public string DownloadClient { get; set; }
        public bool DownloadClientHasPostImportCategory { get; set; }
    }
}
