using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
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
    // Repository). Subscribes to 5 events; each handler builds a ChapterHistory row and
    // writes via the repository.
    //
    // Per-EventType Data column key set per RESEARCH §Q-4 lock:
    //   Grabbed         → Indexer, Size, Age, PublishedDate, DownloadClient, CustomFormatScore, Protocol
    //   DownloadFailed  → DownloadClient, Message, Source, Indexer
    //   Imported        → ChapterFileId, DroppedPath, ImportedPath, Size, DownloadClient, CustomFormatScore
    //   ImportFailed    → DroppedPath, FailureReason, RejectionType
    //   Ignored         → DownloadClient, Message, Indexer
    //
    // Phase 8 cleanup: collapse with HistoryService when Tv/ deletes.
    public class ChapterHistoryService : IChapterHistoryService,
                                         IHandle<ChapterGrabbedEvent>,
                                         IHandle<ChapterImportedEvent>,
                                         IHandle<ChapterDownloadFailedEvent>,
                                         IHandle<DownloadIgnoredEvent>,
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

        public void Handle(DownloadIgnoredEvent message)
        {
            // Sonarr's DownloadIgnoredEvent carries SeriesId + EpisodeIds (TV-shaped). In v1 the
            // manga pipeline does not emit this event — only TV providers do. Bail out so manga
            // history is not populated with TV-side ignores. Phase 8 cleanup: collapse when
            // Tv/ deletes (likely the event will lose its TV-specific fields then).
            if (message.EpisodeIds == null || message.EpisodeIds.Count == 0)
            {
                return;
            }

            _logger.Trace(
                "Skipping TV DownloadIgnoredEvent in ChapterHistoryService — manga ignores route via "
                + "ChapterDownloadFailedEvent path (Phase 6 D-21)");
        }

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
