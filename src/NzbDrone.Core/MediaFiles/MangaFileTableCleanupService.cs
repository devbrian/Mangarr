using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.MediaFiles
{
    using Manga = NzbDrone.Core.Manga.Manga;

    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-03 #1 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/MediaFileTableCleanupService.cs.
    //
    // Cascade-cleanup of orphan ChapterFile rows after disk scan + clear Chapter.ChapterFileId
    // for chapters whose file was deleted. Direct ctor of MangaDiskScanService per D-09-03 #1
    // (TV's DiskScanService:193 calls _mediaFileTableCleanupService.Clean — manga port mirrors).
    //
    // Phase 14 cleanup: collapse with MediaFileTableCleanupService when Tv/ deletes.
    public class MangaFileTableCleanupService : IMangaFileTableCleanupService
    {
        private readonly IChapterFileService _chapterFileService;
        private readonly IChapterService _chapterService;
        private readonly Logger _logger;

        public MangaFileTableCleanupService(IChapterFileService chapterFileService,
                                            IChapterService chapterService,
                                            Logger logger)
        {
            _chapterFileService = chapterFileService;
            _chapterService = chapterService;
            _logger = logger;
        }

        public void Clean(Manga manga, List<string> filesOnDisk)
        {
            var mangaFiles = _chapterFileService.GetFilesByManga(manga.Id);
            var chapters = _chapterService.GetChaptersByManga(manga.Id);

            var filesOnDiskKeys = new HashSet<string>(filesOnDisk, PathEqualityComparer.Instance);

            foreach (var mangaFile in mangaFiles)
            {
                var chapterFile = mangaFile;
                var chapterFilePath = Path.Combine(manga.Path, chapterFile.RelativePath);

                try
                {
                    if (!filesOnDiskKeys.Contains(chapterFilePath))
                    {
                        _logger.Debug("File [{0}] no longer exists on disk, removing from db", chapterFilePath);
                        _chapterFileService.Delete(mangaFile, DeleteMediaFileReason.MissingFromDisk);
                        continue;
                    }

                    if (chapters.None(c => c.ChapterFileId == chapterFile.Id))
                    {
                        _logger.Debug("File [{0}] is not assigned to any chapters, removing from db", chapterFilePath);
                        _chapterFileService.Delete(chapterFile, DeleteMediaFileReason.NoLinkedEpisodes);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to cleanup ChapterFile in DB: {0}", chapterFile.Id);
                }
            }

            foreach (var c in chapters)
            {
                var chapter = c;

                if (chapter.ChapterFileId.GetValueOrDefault() > 0 && mangaFiles.None(f => f.Id == chapter.ChapterFileId.Value))
                {
                    chapter.ChapterFileId = null;
                    _chapterService.UpdateChapter(chapter);
                }
            }
        }
    }
}
