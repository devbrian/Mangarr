using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;
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
    //
    // Plan 06-09 (Rule 2): inherits ModelBase so MangaQueueController can extend
    // RestControllerWithSignalR<MangaQueueResource, MangaQueueItem>. The base type
    // requires `where TModel : ModelBase, new()`. Mirrors the TV Queue (which also
    // inherits ModelBase but is not a DB-mapped entity — pure projection POCO).
    // Inherits Id from ModelBase; the existing HashConverter.GetHashInt31 setter
    // assigns to that inherited property.
    public class MangaQueueItem : ModelBase
    {
        public int? MangaId { get; set; }
        public int? ChapterId { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public NzbDrone.Core.Manga.Chapter Chapter { get; set; }
        public List<NzbDrone.Core.Manga.Chapter> Chapters { get; set; } = new();
        public RemoteChapter RemoteChapter { get; set; }
        public string TranslatedLanguage { get; set; }
        public string ScanlationGroup { get; set; }

        // Carries ReleaseInfo.Source — the gateway's SourceKey (e.g. mangadex / comix.to),
        // the manga-useful per-release identifier now that GatewayIndexer is the sole indexer
        // and Indexer is always "Gateway". Mirrors ChapterHistory.SourceKey / MangaBlocklist.SourceKey.
        public string SourceKey { get; set; }
        public long Size { get; set; }
        public string Title { get; set; }
        public decimal SizeLeft { get; set; }
        public TimeSpan? TimeLeft { get; set; }

        // Phase 36 Plan 06 (D-01 / LOOP-05): ADDITIVE manga-native page-progress channel.
        // Optional/nullable on this manga-side POCO ONLY — NEVER added to the shared
        // DownloadClientItem contract (D-01a hard constraint; Phase 38's GatewayDownloadClient
        // implements that contract bytes-only). Sourced in MangaQueueService.MapQueueItem from
        // the Plan 03 matcher's IMangaDownloadPageProgressSource carrier keyed by DownloadId
        // (NOT the now-retired in-process ChapterDownloadState row — Phase 39 RETIRE-01). Null on
        // the gateway path → the Queue caption falls back to bytes/% (D-01b — the byte/% bar still
        // drives the fill; this is presentational only).
        public int? TotalPages { get; set; }
        public int? CompletedPages { get; set; }
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
