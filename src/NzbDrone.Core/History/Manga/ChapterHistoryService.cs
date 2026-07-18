using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/History/HistoryService.cs.
    //
    // Event-driven service (RESEARCH Anti-Pattern: NEVER write history from inside the
    // Repository). Subscribes to 6 events; each handler builds a ChapterHistory row and
    // writes via the repository. ImportFailed (import-stage exception) and Ignored (completed
    // but deliberately not imported — already owned / not an upgrade) were wired after debug
    // session reimport-no-history-event found their enum members had no writer.
    //
    // Per-EventType Data column key set per RESEARCH §Q-4 lock:
    //   Grabbed         → Indexer, Size, Age, PublishedDate, DownloadClient, CustomFormatScore, Protocol
    //   DownloadFailed  → DownloadClient, Message, Source, Indexer
    //   Imported        → ChapterFileId, DroppedPath, ImportedPath, Size, DownloadClient, CustomFormatScore, source
    //   ImportFailed    → DroppedPath, FailureReason, RejectionType
    //   Ignored         → DownloadClient, Message, Indexer
    //
    // Phase 8 cleanup: collapse with HistoryService when Tv/ deletes.
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — DownloadIgnoredEvent
    // (TV-only) handler stripped per Plan 15-10 Download/DownloadIgnoredEvent.cs DELETE.
    // Manga ignores route via MangaQueueActionController -> IIgnoredDownloadService directly.
    public class ChapterHistoryService : IChapterHistoryService,
                                         IHandle<ChapterGrabbedEvent>,
                                         IHandle<ChapterImportedEvent>,
                                         IHandle<ChapterImportFailedEvent>,
                                         IHandle<ChapterImportIgnoredEvent>,
                                         IHandle<ChapterDownloadFailedEvent>,
                                         IHandle<MangaDeletedEvent>
    {
        private readonly IChapterHistoryRepository _repository;
        private readonly Logger _logger;

        public ChapterHistoryService(IChapterHistoryRepository repository, Logger logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public PagingSpec<ChapterHistory> Paged(PagingSpec<ChapterHistory> pagingSpec, int[] languages)
        {
            return _repository.GetPaged(pagingSpec, languages);
        }

        public ChapterHistory MostRecentForChapter(int chapterId)
        {
            return _repository.MostRecentForChapter(chapterId);
        }

        public List<ChapterHistory> FindByChapterId(int chapterId)
        {
            return _repository.FindByChapterId(chapterId);
        }

        public ChapterHistory MostRecentForDownloadId(string downloadId)
        {
            return _repository.MostRecentForDownloadId(downloadId);
        }

        public ChapterHistory Get(int historyId)
        {
            return _repository.Get(historyId);
        }

        public List<ChapterHistory> GetByManga(int mangaId, ChapterHistoryEventType? eventType)
        {
            return _repository.GetByManga(mangaId, eventType);
        }

        public List<ChapterHistory> Find(string downloadId, ChapterHistoryEventType eventType)
        {
            return _repository.FindByDownloadId(downloadId).Where(h => h.EventType == eventType).ToList();
        }

        public List<ChapterHistory> FindByDownloadId(string downloadId)
        {
            return _repository.FindByDownloadId(downloadId);
        }

        public string FindDownloadId(ChapterImportedEvent imported)
        {
            // V1 simplification: ChapterImportedEvent already carries DownloadClientItem.DownloadId
            // when populated by Plan 06-07 ImportApprovedChapters. If absent, walk back through
            // ChapterHistory looking for the most recent Grabbed row that has not yet seen an
            // Imported event with the same DownloadId.
            if (imported?.Chapter == null)
            {
                return null;
            }

            var chapterHistory = _repository.FindByChapterId(imported.Chapter.Id);

            var processedDownloadIds = chapterHistory
                .Where(h => h.EventType != ChapterHistoryEventType.Grabbed && h.DownloadId.IsNotNullOrWhiteSpace())
                .Select(h => h.DownloadId)
                .ToHashSet();

            var stillDownloading = chapterHistory
                .Where(h => h.EventType == ChapterHistoryEventType.Grabbed
                            && h.DownloadId.IsNotNullOrWhiteSpace()
                            && !processedDownloadIds.Contains(h.DownloadId))
                .ToList();

            return stillDownloading.Count == 1 ? stillDownloading[0].DownloadId : null;
        }

        public List<ChapterHistory> Since(DateTime date, ChapterHistoryEventType? eventType)
        {
            return _repository.Since(date, eventType);
        }

        public void Handle(ChapterGrabbedEvent message)
        {
            // RESEARCH Pattern 3 (lines 487-516): build a Grabbed row carrying the manga
            // release identity triple (SourceKey, ReleaseGuid, Title) plus the per-event
            // Data dictionary keys per Q-4 schema lock.
            var release = message.RemoteChapter?.Release;
            if (release == null || message.RemoteChapter.Manga == null)
            {
                return;
            }

            foreach (var chapter in message.RemoteChapter.Chapters ?? new List<NzbDrone.Core.Manga.Chapter>())
            {
                var history = new ChapterHistory
                {
                    EventType = ChapterHistoryEventType.Grabbed,
                    Date = DateTime.UtcNow,
                    SourceTitle = release.Title,
                    MangaId = message.RemoteChapter.Manga.Id,
                    ChapterId = chapter.Id,
                    DownloadId = message.DownloadId,
                    TranslatedLanguage = release.TranslatedLanguage,
                    ScanlationGroup = release.ScanlationGroup,
                    SourceKey = release.Indexer,
                    ReleaseGuid = release.Guid,
                    Successful = true
                };

                history.Data.Add("Indexer", release.Indexer ?? string.Empty);
                history.Data.Add("Age", release.Age.ToString());
                history.Data.Add("PublishedDate", release.PublishDate.ToUniversalTime().ToString("s") + "Z");
                history.Data.Add("DownloadClient", message.DownloadClient ?? string.Empty);
                history.Data.Add("Size", release.Size.ToString());
                history.Data.Add("Protocol", ((int)release.DownloadProtocol).ToString());
                history.Data.Add("CustomFormatScore", message.RemoteChapter.CustomFormatScore.ToString());

                _repository.Insert(history);
            }
        }

        public void Handle(ChapterImportedEvent message)
        {
            if (message?.Chapter == null || message.Manga == null)
            {
                return;
            }

            // Pitfall 4 GUARD: this handler runs on a published ChapterImportedEvent, which
            // Plan 06-07 ImportApprovedChapters MUST publish AFTER the ChapterFile DB commit
            // and filesystem move. ChapterFile.Id != 0 is the post-commit signal.
            var history = new ChapterHistory
            {
                EventType = ChapterHistoryEventType.Imported,
                Date = DateTime.UtcNow,
                SourceTitle = message.SourcePath ?? message.ChapterFile?.RelativePath ?? string.Empty,
                MangaId = message.Manga.Id,
                ChapterId = message.Chapter.Id,
                DownloadId = message.DownloadClientItem?.DownloadId,
                TranslatedLanguage = message.ChapterFile?.TranslatedLanguage,
                ScanlationGroup = message.ChapterFile?.ScanlationGroup,
                Successful = true
            };

            if (message.ChapterFile != null)
            {
                history.Data.Add("ChapterFileId", message.ChapterFile.Id.ToString());
                history.Data.Add("Size", message.ChapterFile.Size.ToString());
                history.Data.Add("ImportedPath", message.ChapterFile.Path ?? string.Empty);
            }

            history.Data.Add("DroppedPath", message.SourcePath ?? string.Empty);
            history.Data.Add("DownloadClient", message.DownloadClientItem?.DownloadClientInfo?.Type ?? string.Empty);

            // The originating gateway source (comix / kagane / mangadex) the download was actually
            // served from — carried on the completed DownloadClientItem (GatewayDownloadClient sets
            // it from DownloadJob.sourceKey; null for other clients). Persist it so the source is
            // visible in History without digging through gateway logs.
            history.Data.Add("source", message.DownloadClientItem?.MangaSourceKey ?? string.Empty);

            _repository.Insert(history);
        }

        public void Handle(ChapterImportFailedEvent message)
        {
            // ImportFailed row — a completed download that finished but threw during the import
            // stage (RootFolderNotFound / recycle-bin / move failure). The per-decision catch blocks
            // in ImportApprovedChapters publish this event; before this handler was wired the failure
            // was silent in persisted History. Per-EventType Data key set (Q-4): DroppedPath,
            // FailureReason, RejectionType.
            if (message?.Chapter == null || message.Manga == null)
            {
                return;
            }

            var history = new ChapterHistory
            {
                EventType = ChapterHistoryEventType.ImportFailed,
                Date = DateTime.UtcNow,
                SourceTitle = message.SourcePath ?? string.Empty,
                MangaId = message.Manga.Id,
                ChapterId = message.Chapter.Id,
                DownloadId = message.DownloadClientItem?.DownloadId,
                Successful = false
            };

            history.Data.Add("DroppedPath", message.SourcePath ?? string.Empty);
            history.Data.Add("FailureReason", message.FailureReason ?? string.Empty);
            history.Data.Add("RejectionType", string.Empty);

            _repository.Insert(history);
        }

        public void Handle(ChapterImportIgnoredEvent message)
        {
            // Ignored row — a completed download that finished fine but was DELIBERATELY not imported
            // (the chapter is already owned, or the release is not an upgrade). NOT a failure — neutral
            // outcome (debug: reimport-no-history-event). Per-EventType Data key set (Q-4): DownloadClient,
            // Message, Indexer (+ RejectionType for diagnostics).
            if (message?.Chapter == null || message.Manga == null)
            {
                return;
            }

            var history = new ChapterHistory
            {
                EventType = ChapterHistoryEventType.Ignored,
                Date = DateTime.UtcNow,
                SourceTitle = message.SourcePath ?? string.Empty,
                MangaId = message.Manga.Id,
                ChapterId = message.Chapter.Id,
                DownloadId = message.DownloadClientItem?.DownloadId,
                SourceKey = message.Indexer,
                TranslatedLanguage = message.TranslatedLanguage,
                ScanlationGroup = message.ScanlationGroup,
                Successful = false
            };

            history.Data.Add("DownloadClient", message.DownloadClientItem?.DownloadClientInfo?.Type ?? string.Empty);
            history.Data.Add("Message", message.Reason ?? string.Empty);
            history.Data.Add("Indexer", message.Indexer ?? string.Empty);
            history.Data.Add("RejectionType", message.RejectionType ?? string.Empty);

            _repository.Insert(history);
        }

        public void Handle(ChapterDownloadFailedEvent message)
        {
            // Plan 06-01 extended ChapterDownloadFailedEvent with optional Source/DownloadClient/
            // Release/SourceTitle init-only properties. Phase 4 emit sites populate via object-
            // initializer where data is in scope; null otherwise. Tolerate null.
            var history = new ChapterHistory
            {
                EventType = ChapterHistoryEventType.DownloadFailed,
                Date = DateTime.UtcNow,
                SourceTitle = message.SourceTitle ?? message.Release?.Title ?? string.Empty,
                MangaId = message.MangaId,
                ChapterId = message.ChapterId,
                TranslatedLanguage = message.Release?.TranslatedLanguage,
                ScanlationGroup = message.Release?.ScanlationGroup,
                SourceKey = message.Release?.Indexer,
                ReleaseGuid = message.Release?.Guid,
                Successful = false
            };

            history.Data.Add("DownloadClient", message.DownloadClient ?? string.Empty);
            history.Data.Add("Message", message.FailureReason ?? string.Empty);
            history.Data.Add("Source", message.Source ?? string.Empty);
            history.Data.Add("Indexer", message.Release?.Indexer ?? string.Empty);

            _repository.Insert(history);
        }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — Handle(DownloadIgnoredEvent)
        // stripped (event class deleted; manga ignores route via ChapterDownloadFailedEvent path
        // per Phase 6 D-21).

        public void Handle(MangaDeletedEvent message)
        {
            // Cascade-delete history rows when the parent manga is removed. Mirrors TV's
            // HistoryService.Handle(SeriesDeletedEvent) at HistoryService.cs:368-371.
            if (message?.Manga == null)
            {
                return;
            }

            _repository.DeleteForManga(message.Manga.Id);
        }
    }
}
