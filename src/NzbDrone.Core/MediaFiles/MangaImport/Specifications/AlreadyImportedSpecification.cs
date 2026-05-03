using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.History.Manga;

namespace NzbDrone.Core.MediaFiles.MangaImport.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/
    // AlreadyImportedSpecification.cs.
    //
    // BL-01 GUARD (Phase 5 LEARNINGS): queries ChapterHistory.ChapterId via the new
    // IChapterHistoryService.FindByChapterId — NEVER IHistoryService.FindByEpisodeId
    // (that path was the cross-domain ID-collision bug class — Episode.Id and Chapter.Id
    // come from independent SQLite autoincrement sequences).
    //
    // Rejects re-import when:
    //   1. The DownloadClientItem.DownloadId has a prior Imported row in ChapterHistory
    //      for this chapter (it's the same download, processed again).
    //   2. AND no later Grabbed row exists for the same download (regrabbed-after-import
    //      means the user explicitly wants to re-import; allow that).
    //
    // EventType is ChapterHistoryEventType.Imported (Phase 6 D-21 — manga collapses TV's
    // DownloadFolderImported + EpisodeFileRenamed + SeriesFolderImported into a single
    // Imported event since manga has no rename / season-folder import paths in v1).
    //
    // Phase 8 cleanup: collapse with TV AlreadyImportedSpecification when Tv/ deletes.
    public class AlreadyImportedSpecification : IMangaImportDecisionEngineSpecification
    {
        private readonly IChapterHistoryService _chapterHistoryService;
        private readonly Logger _logger;

        public AlreadyImportedSpecification(IChapterHistoryService chapterHistoryService, Logger logger)
        {
            _chapterHistoryService = chapterHistoryService;
            _logger = logger;
        }

        public MangaImportSpecDecision IsSatisfiedBy(LocalChapter localChapter, DownloadClientItem downloadClientItem)
        {
            if (downloadClientItem == null)
            {
                _logger.Debug("No download client information is available, skipping");
                return MangaImportSpecDecision.Accept();
            }

            if (downloadClientItem.DownloadId.IsNullOrWhiteSpace())
            {
                return MangaImportSpecDecision.Accept();
            }

            foreach (var chapter in localChapter.Chapters)
            {
                // Manga sibling of TV's "if (!episode.HasFile) continue" — chapters with no
                // ChapterFile linked have nothing to be "already imported" against.
                if (chapter.ChapterFileId.GetValueOrDefault() == 0)
                {
                    _logger.Trace("Skipping already-imported check for chapter {0} without file", chapter.Id);
                    continue;
                }

                // BL-01 GUARD: ChapterHistory.ChapterId, NOT EpisodeHistory.EpisodeId.
                var chapterHistory = _chapterHistoryService.FindByChapterId(chapter.Id);

                var lastImported = chapterHistory.FirstOrDefault(h =>
                    h.DownloadId == downloadClientItem.DownloadId &&
                    h.EventType == ChapterHistoryEventType.Imported);
                var lastGrabbed = chapterHistory.FirstOrDefault(h =>
                    h.DownloadId == downloadClientItem.DownloadId &&
                    h.EventType == ChapterHistoryEventType.Grabbed);

                if (lastImported == null)
                {
                    _logger.Trace("Chapter {0} has no Imported history for this download", chapter.Id);
                    continue;
                }

                if (lastGrabbed != null && lastGrabbed.Date.After(lastImported.Date))
                {
                    // Regrabbed after import — allow re-import.
                    _logger.Trace("Chapter {0} was grabbed again after importing; allowing re-import", chapter.Id);
                    continue;
                }

                _logger.Debug("Chapter {0} already imported at {1}", chapter.Id, lastImported.Date);
                return MangaImportSpecDecision.Reject(
                    ImportRejectionReason.ChapterAlreadyImported,
                    "Chapter {0} already imported at {1}",
                    chapter.Id,
                    lastImported.Date.ToLocalTime());
            }

            return MangaImportSpecDecision.Accept();
        }
    }
}
