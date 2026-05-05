using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer.Manga;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit (no-sibling/RenameEpisodeFileService).
    // Role-match analog: src/NzbDrone.Core/MediaFiles/RenameEpisodeFileService.cs (TV).
    //
    // Mirrors IRenameEpisodeFileService's three GetRenamePreviews overloads (per-manga,
    // per-chapter-number, multi-manga) and the RenameSeriesCommand handler (manga peer:
    // RenameMangaCommand, Plan 02-07). Drives renames via IMoveChapterFiles
    // (ChapterFileMovingService, Plan 02-15), publishes ChapterFileRenamedEvent
    // (Plan 02-04) per file and MangaRenamedEvent (Plan 02-10) per manga at end.
    //
    // Slim ctor relative to TV: chapter-number is decimal (Chapter.ChapterNumber per
    // Phase 2 D-12 widen) not int; the per-chapter-number overload accepts decimal? to
    // match. Skips IExecute<RenameFilesCommand> — that command carries SeriesId (TV-only
    // shape) and the manga-side bulk-rename-by-file-id flow has no consumer yet
    // (Phase 8 audit-driven backfill is service+interface only; per-file rename UX
    // wiring is downstream). Reuses RenameCompletedEvent (single shared event).
    //
    // Phase 8 cleanup: collapse with RenameEpisodeFileService when Tv/ deletes.
    public interface IRenameChapterFileService
    {
        List<RenameChapterFilePreview> GetRenamePreviews(int mangaId);
        List<RenameChapterFilePreview> GetRenamePreviews(int mangaId, decimal? chapterNumber);
        List<RenameChapterFilePreview> GetRenamePreviews(List<int> mangaIds);
    }

    public class RenameChapterFileService : IRenameChapterFileService,
                                            IExecute<RenameMangaCommand>
    {
        private readonly IMangaService _mangaService;
        private readonly IChapterFileService _chapterFileService;
        private readonly IMoveChapterFiles _chapterFileMover;
        private readonly IEventAggregator _eventAggregator;
        private readonly IChapterService _chapterService;
        private readonly IBuildMangaFileNames _filenameBuilder;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public RenameChapterFileService(IMangaService mangaService,
                                        IChapterFileService chapterFileService,
                                        IMoveChapterFiles chapterFileMover,
                                        IEventAggregator eventAggregator,
                                        IChapterService chapterService,
                                        IBuildMangaFileNames filenameBuilder,
                                        IDiskProvider diskProvider,
                                        Logger logger)
        {
            _mangaService = mangaService;
            _chapterFileService = chapterFileService;
            _chapterFileMover = chapterFileMover;
            _eventAggregator = eventAggregator;
            _chapterService = chapterService;
            _filenameBuilder = filenameBuilder;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public List<RenameChapterFilePreview> GetRenamePreviews(int mangaId)
        {
            var manga = _mangaService.GetManga(mangaId);
            var chapters = _chapterService.GetChaptersByManga(mangaId);
            var files = _chapterFileService.GetFilesByManga(mangaId);

            return GetPreviews(manga, chapters, files)
                .OrderByDescending(c => c.ChapterNumbers.FirstOrDefault())
                .ToList();
        }

        public List<RenameChapterFilePreview> GetRenamePreviews(int mangaId, decimal? chapterNumber)
        {
            var manga = _mangaService.GetManga(mangaId);
            var chapters = _chapterService.GetChaptersByManga(mangaId);
            var files = _chapterFileService.GetFilesByManga(mangaId);

            if (chapterNumber.HasValue)
            {
                var matchingChapterIds = chapters
                    .Where(c => c.ChapterNumber == chapterNumber.Value)
                    .Select(c => c.Id)
                    .ToHashSet();

                chapters = chapters.Where(c => matchingChapterIds.Contains(c.Id)).ToList();
                files = files.Where(f => chapters.Any(c => c.ChapterFileId == f.Id)).ToList();
            }

            return GetPreviews(manga, chapters, files)
                .OrderByDescending(c => c.ChapterNumbers.FirstOrDefault())
                .ToList();
        }

        public List<RenameChapterFilePreview> GetRenamePreviews(List<int> mangaIds)
        {
            var mangaList = _mangaService.GetManga(mangaIds);
            var previews = new List<RenameChapterFilePreview>();

            foreach (var manga in mangaList)
            {
                var chapters = _chapterService.GetChaptersByManga(manga.Id);
                var files = _chapterFileService.GetFilesByManga(manga.Id);
                previews.AddRange(GetPreviews(manga, chapters, files));
            }

            return previews
                .OrderByDescending(c => c.MangaId)
                .ThenByDescending(c => c.ChapterNumbers.FirstOrDefault())
                .ToList();
        }

        private IEnumerable<RenameChapterFilePreview> GetPreviews(MangaModel manga, List<Chapter> chapters, List<ChapterFile> files)
        {
            foreach (var f in files)
            {
                var file = f;
                var chaptersInFile = chapters.Where(c => c.ChapterFileId == file.Id).ToList();
                var chapterFilePath = Path.Combine(manga.Path, file.RelativePath);

                if (!chaptersInFile.Any())
                {
                    _logger.Warn("File ({0}) is not linked to any chapters", chapterFilePath);
                    continue;
                }

                var newPath = _filenameBuilder.BuildFilePath(chaptersInFile, manga, release: null, extension: Path.GetExtension(chapterFilePath));

                if (!chapterFilePath.PathEquals(newPath, StringComparison.Ordinal))
                {
                    yield return new RenameChapterFilePreview
                    {
                        MangaId = manga.Id,
                        ChapterIds = chaptersInFile.Select(c => c.Id).ToList(),
                        ChapterNumbers = chaptersInFile.Select(c => c.ChapterNumber).ToList(),
                        ChapterFileId = file.Id,
                        ExistingPath = file.RelativePath,
                        NewPath = manga.Path.GetRelativePath(newPath)
                    };
                }
            }
        }

        private List<RenamedChapterFile> RenameFiles(List<ChapterFile> chapterFiles, MangaModel manga)
        {
            var renamed = new List<RenamedChapterFile>();

            foreach (var chapterFile in chapterFiles)
            {
                var previousRelativePath = chapterFile.RelativePath;
                var previousPath = Path.Combine(manga.Path, chapterFile.RelativePath);

                try
                {
                    _logger.Debug("Renaming chapter file: {0}", chapterFile);
                    _chapterFileMover.MoveChapterFile(chapterFile, manga);

                    _chapterFileService.Update(chapterFile);

                    renamed.Add(new RenamedChapterFile
                                {
                                    ChapterFile = chapterFile,
                                    PreviousRelativePath = previousRelativePath,
                                    PreviousPath = previousPath
                                });

                    _logger.Debug("Renamed chapter file: {0}", chapterFile);

                    _eventAggregator.PublishEvent(new ChapterFileRenamedEvent(chapterFile, previousPath));
                }
                catch (FileAlreadyExistsException ex)
                {
                    _logger.Warn("File not renamed, there is already a file at the destination: {0}", ex.Filename);
                }
                catch (SameFilenameException ex)
                {
                    _logger.Debug("File not renamed, source and destination are the same: {0}", ex.Filename);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to rename file {0}", previousPath);
                }
            }

            if (renamed.Any())
            {
                _diskProvider.RemoveEmptySubfolders(manga.Path);

                _eventAggregator.PublishEvent(new MangaRenamedEvent(manga, renamed));
            }

            return renamed;
        }

        public void Execute(RenameMangaCommand message)
        {
            _logger.Debug("Renaming all files for selected manga");
            var mangaToRename = _mangaService.GetManga(message.MangaIds);

            foreach (var manga in mangaToRename)
            {
                var chapterFiles = _chapterFileService.GetFilesByManga(manga.Id);
                _logger.ProgressInfo("Renaming all files in manga: {0}", manga.Title);
                var renamedFiles = RenameFiles(chapterFiles, manga);
                _logger.ProgressInfo("{0} chapter files renamed for {1}", renamedFiles.Count, manga.Title);
            }

            _eventAggregator.PublishEvent(new RenameCompletedEvent());
        }
    }
}
