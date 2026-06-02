using Mangarr.Api.V5.Manga.Subresources;
using Mangarr.Http.REST;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Queue.Manga;

namespace Mangarr.Api.V5.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 resource per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Mangarr.Api.V5/Queue/QueueResource.cs.
    //
    // Manga sibling preserves: PascalCase POCO + RestResource Id + ToResource() static mapper.
    //
    // Manga sibling diverges from QueueResource:
    //   * No QualityModel / Languages enum (manga has no quality model per Phase 5 D-04;
    //     TranslatedLanguage is BCP-47 string per Phase 3 D-Q4).
    //   * Adds ScanlationGroup as a first-class field.
    //   * Manga / Chapter subresources hydrated by the controller (not the service).
    //   * Drops RemoteEpisode (does NOT leak the in-process EF reference); flattens to wire shape.
    //
    // Phase 8 cleanup: collapse with QueueResource when Tv/ deletes.
    public class MangaQueueResource : RestResource
    {
        public int? MangaId { get; set; }
        public int? ChapterId { get; set; }
        public List<int>? ChapterIds { get; set; }
        public string? TranslatedLanguage { get; set; }
        public string? ScanlationGroup { get; set; }
        public long Size { get; set; }
        public string? Title { get; set; }
        public decimal SizeLeft { get; set; }
        public TimeSpan? TimeLeft { get; set; }

        // Phase 36 Plan 06 (D-01 / LOOP-05): ADDITIVE manga-native page-progress wire fields. Null
        // on the gateway path (Phase 38 — no IMangaDownloadPageProgressSource reports the id), so
        // QueueRow.tsx falls back to bytes/% gracefully (D-01a/D-01b). NOT a DownloadClientItem field.
        public int? TotalPages { get; set; }
        public int? CompletedPages { get; set; }
        public DateTime? EstimatedCompletionTime { get; set; }
        public DateTime? Added { get; set; }
        public string? Status { get; set; }
        public string? TrackedDownloadStatus { get; set; }
        public string? TrackedDownloadState { get; set; }
        public List<TrackedDownloadStatusMessage>? StatusMessages { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DownloadId { get; set; }
        public string? Indexer { get; set; }
        public string? OutputPath { get; set; }
        public DownloadProtocol Protocol { get; set; }
        public string? DownloadClient { get; set; }
        public bool DownloadClientHasPostImportCategory { get; set; }
        public MangaSubresource? Manga { get; set; }
        public ChapterSubresource? Chapter { get; set; }
    }

    public static class MangaQueueResourceMapper
    {
        public static MangaQueueResource? ToResource(this MangaQueueItem? model)
        {
            if (model == null)
            {
                return null;
            }

            return new MangaQueueResource
            {
                Id = model.Id,
                MangaId = model.MangaId,
                ChapterId = model.ChapterId,
                ChapterIds = model.Chapters?.Select(c => c.Id).ToList(),
                TranslatedLanguage = model.TranslatedLanguage,
                ScanlationGroup = model.ScanlationGroup,
                Size = model.Size,
                Title = model.Title,
                SizeLeft = model.SizeLeft,
                TimeLeft = model.TimeLeft,
                TotalPages = model.TotalPages,
                CompletedPages = model.CompletedPages,
                EstimatedCompletionTime = model.EstimatedCompletionTime,
                Added = model.Added,
                Status = model.Status,
                TrackedDownloadStatus = model.TrackedDownloadStatus,
                TrackedDownloadState = model.TrackedDownloadState,
                StatusMessages = model.StatusMessages,
                ErrorMessage = model.ErrorMessage,
                DownloadId = model.DownloadId,
                Indexer = model.Indexer,
                OutputPath = model.OutputPath,
                Protocol = model.Protocol,
                DownloadClient = model.DownloadClient,
                DownloadClientHasPostImportCategory = model.DownloadClientHasPostImportCategory,
                Manga = model.Manga == null ? null : new MangaSubresource
                {
                    Id = model.Manga.Id,
                    Title = model.Manga.Title
                },
                Chapter = model.Chapter == null ? null : new ChapterSubresource
                {
                    Id = model.Chapter.Id,
                    MangaId = model.Chapter.MangaId,
                    ChapterNumber = model.Chapter.ChapterNumber,
                    Title = model.Chapter.Title
                }
            };
        }
    }
}
