using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 D-05 — single source of truth for "is this chapter in flight."
    /// Hybrid model: this DB row tracks lifecycle + manifest cache + scratch-dir pointer;
    /// per-page bytes live in <c>&lt;Config.DownloadScratchPath&gt;/&lt;Id&gt;/&lt;NNNN&gt;.&lt;ext&gt;</c>.
    /// Phase 6 reads <c>Status=Completed</c> rows for <c>ImportApprovedChapters</c> dispatch.
    /// Phase 8 cleanup: stays as-is (manga-shaped from Day 1).
    ///
    /// Own <see cref="ModelBase"/> subclass (NOT extending <c>ProviderStatusBase</c>) — mirrors
    /// Phase 3 <see cref="NzbDrone.Core.Indexers.IndexerSourceStatus"/> precedent (Q-2 Option A)
    /// so Phase 8 cleanup is a clean DROP when <c>Tv/</c> deletes.
    /// </summary>
    public class ChapterDownloadState : ModelBase
    {
        public int MangaId { get; set; }
        public int ChapterId { get; set; }
        public string Title { get; set; }                       // for DownloadClientItem.Title (D-11/D-12)

        public string RemoteChapterJson { get; set; }           // serialized RemoteEpisode (Phase 8 → RemoteChapter)
        public string ManifestJson { get; set; }                // serialized ChapterManifest (D-02); null on resume before re-fetch
        public DateTime? ManifestExpiresAt { get; set; }        // D-03 informational; reactive re-fetch is canonical

        public int TotalPages { get; set; }
        public int CompletedPages { get; set; }
        public long EstimatedSizeBytes { get; set; }            // D-12 progress projection

        public string ScratchDir { get; set; }                  // <Config.DownloadScratchPath>/<Id>/
        public string StagingPath { get; set; }                 // OutputPath after Status=Completed (D-11)

        public ChapterDownloadStatus Status { get; set; }
        public string FailureReason { get; set; }
        public DateTime? RetentionUntil { get; set; }           // D-08 — null while non-terminal

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
