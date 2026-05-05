using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles
{
    using Manga = NzbDrone.Core.Manga.Manga;

    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: MediaFileService. Phase 8 cleanup: collapse on Tv/ deletion.
    // Cascade delete on MangaDeletedEvent mirrors TV's IHandleAsync<SeriesDeletedEvent>.
    // Phase 9 D-09-03 #3 + 09-01 audit: GetFilesByMangaIds (gap-01), FilterExistingFiles
    // instance + static (gap-02), GetFilesWithRelativePath (gap-05) added for parity with
    // IMediaFileService — required by Plan 09-06 MangaDiskScanService per RESEARCH §Pitfall 3.
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

        // Phase 9 D-09-03 #3 + 09-01 audit gap-01: bulk get-by-multiple-parent-IDs.
        // Mirrors MediaFileService.GetFilesBySeriesIds.
        public List<ChapterFile> GetFilesByMangaIds(List<int> mangaIds)
        {
            return _chapterFileRepository.GetFilesByMangaIds(mangaIds);
        }

        // Phase 9 D-09-03 #3 + 09-01 audit gap-05: relative-path collision detection.
        // Mirrors MediaFileService.GetFilesWithRelativePath.
        public List<ChapterFile> GetFilesWithRelativePath(int mangaId, string relativePath)
        {
            return _chapterFileRepository.GetFilesWithRelativePath(mangaId, relativePath);
        }

        // Phase 9 D-09-03 #3 + 09-01 audit gap-02: instance overload that auto-fetches the
        // manga's existing files and delegates to the static helper. Mirrors
        // MediaFileService.FilterExistingFiles(List<string>, Series) at MediaFileService.cs:95-100.
        public List<string> FilterExistingFiles(List<string> files, Manga manga)
        {
            var mangaFiles = GetFilesByManga(manga.Id);

            return FilterExistingFiles(files, mangaFiles, manga);
        }

        public void HandleAsync(MangaDeletedEvent message)
        {
            // Cascade delete chapter files when the parent manga is removed.
            // Mirrors TV's MediaFileService.HandleAsync(SeriesDeletedEvent).
            _chapterFileRepository.DeleteForManga(message.Manga.Id);
        }

        // Phase 9 D-09-03 #3 + 09-01 audit gap-02: static helper — callers (e.g.,
        // MangaDiskScanService) can pre-fetch the file list to avoid double-query when they
        // already have the rows. Mirrors MediaFileService.cs:122-132 verbatim.
        public static List<string> FilterExistingFiles(List<string> files, List<ChapterFile> mangaFiles, Manga manga)
        {
            var mangaFilePaths = mangaFiles.Select(f => Path.Combine(manga.Path, f.RelativePath)).ToList();

            if (!mangaFilePaths.Any())
            {
                return files;
            }

            return files.Except(mangaFilePaths, PathEqualityComparer.Instance).ToList();
        }
    }
}
