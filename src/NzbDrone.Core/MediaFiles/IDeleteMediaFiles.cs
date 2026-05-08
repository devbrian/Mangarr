using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — relocated and reduced from
    // deleted MediaFileDeletionService.cs (TV-shape Series/EpisodeFile API). Manga-shape facade
    // around IRecycleBinProvider + IChapterFileService for V5 controller delete flow.
    // V1 sends file to recycle bin then drops the DB row; ChapterFileController wires this directly.
    public interface IDeleteMediaFiles
    {
        void DeleteChapterFile(MangaModel manga, ChapterFile chapterFile);
    }

    public class DeleteMediaFiles : IDeleteMediaFiles
    {
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IChapterFileService _chapterFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public DeleteMediaFiles(IRecycleBinProvider recycleBinProvider,
                                IChapterFileService chapterFileService,
                                IDiskProvider diskProvider,
                                IConfigService configService,
                                Logger logger)
        {
            _recycleBinProvider = recycleBinProvider;
            _chapterFileService = chapterFileService;
            _diskProvider = diskProvider;
            _configService = configService;
            _logger = logger;
        }

        public void DeleteChapterFile(MangaModel manga, ChapterFile chapterFile)
        {
            var fullPath = System.IO.Path.Combine(manga.Path, chapterFile.RelativePath);

            if (_diskProvider.FileExists(fullPath))
            {
                _logger.Info("Deleting chapter file: {0}", fullPath);
                try
                {
                    _recycleBinProvider.DeleteFile(fullPath, manga.Path);
                }
                catch (System.Exception ex)
                {
                    _logger.Error(ex, "Unable to delete chapter file: {0}", fullPath);
                    throw new RecycleBinException(ex.Message);
                }
            }

            _chapterFileService.Delete(chapterFile, DeleteMediaFileReason.Manual);
        }
    }
}
