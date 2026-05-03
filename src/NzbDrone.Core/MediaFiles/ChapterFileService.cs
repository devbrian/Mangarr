using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: MediaFileService. Phase 8 cleanup: collapse on Tv/ deletion.
    // Cascade delete on MangaDeletedEvent mirrors TV's IHandleAsync<SeriesDeletedEvent>.
    public class ChapterFileService : IChapterFileService, IHandleAsync<MangaDeletedEvent>
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IChapterFileRepository _chapterFileRepository;
        private readonly Logger _logger;

        public ChapterFileService(IChapterFileRepository chapterFileRepository, IEventAggregator eventAggregator, Logger logger)
        {
            _chapterFileRepository = chapterFileRepository;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public ChapterFile Add(ChapterFile chapterFile)
        {
            var addedFile = _chapterFileRepository.Insert(chapterFile);
            _eventAggregator.PublishEvent(new ChapterFileAddedEvent(addedFile));
            return addedFile;
        }

        public void Update(ChapterFile chapterFile)
        {
            _chapterFileRepository.Update(chapterFile);
        }

        public void Update(List<ChapterFile> chapterFiles)
        {
            _chapterFileRepository.UpdateMany(chapterFiles);
        }

        public void Delete(ChapterFile chapterFile, DeleteMediaFileReason reason)
        {
            _chapterFileRepository.Delete(chapterFile);
            _eventAggregator.PublishEvent(new ChapterFileDeletedEvent(chapterFile, reason));
        }

        public ChapterFile Get(int id)
        {
            return _chapterFileRepository.Get(id);
        }

        public List<ChapterFile> Get(IEnumerable<int> ids)
        {
            return _chapterFileRepository.Get(ids).ToList();
        }

        public List<ChapterFile> GetFilesByChapter(int chapterId)
        {
            return _chapterFileRepository.GetFilesByChapter(chapterId);
        }

        public List<ChapterFile> GetFilesByManga(int mangaId)
        {
            return _chapterFileRepository.GetFilesByManga(mangaId);
        }

        public void HandleAsync(MangaDeletedEvent message)
        {
            // Cascade delete chapter files when the parent manga is removed.
            // Mirrors TV's MediaFileService.HandleAsync(SeriesDeletedEvent).
            _chapterFileRepository.DeleteForManga(message.Manga.Id);
        }
    }
}
