using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: MediaFileService. Phase 8 cleanup: collapse on Tv/ deletion.
    // Cascade delete on MangaDeletedEvent mirrors TV's IHandleAsync<SeriesDeletedEvent>.
    // Phase 9 D-09-03 #3 + 09-01 audit: GetFilesByMangaIds (gap-01), FilterExistingFiles
    // instance + static (gap-02), GetFilesWithRelativePath (gap-05) added for parity with
    // IMediaFileService — required by Plan 09-06 MangaDiskScanService per RESEARCH §Pitfall 3.
    //
    // Phase 11 Plan 11-06 (sub-wave C, audit gap-IExecute-DeleteMangaFiles close-out): adds
    // IExecute<DeleteMangaFilesCommand> to ChapterFileService. Closes the silent
    // UnknownCommandExecutor fallback that has been swallowing manga bulk-delete-files
    // commands since Phase 8 cluster-02 Plan 02-01 (DeleteMangaFilesCommand class shipped
    // + IExecute handler missing — Risk 1 in 11-RESEARCH.md §12).
    // Role-match analog: src/NzbDrone.Core/MediaFiles/MediaFileDeletionService.cs:102-175.
    //
    // Pitfall 4 contract — recycle FIRST, DB row delete SECOND. The inverted order leaks the
    // file path on disk if _recycleBinProvider.DeleteFile throws (mirrors TV lines 154-163
    // verbatim). Per-mangaId try/catch isolates partial failures; CommandResult.Indeterminate
    // reported on each guard-clause continue (matches TV lines 124, 131, 138, 159, 172).
    //
    // Phase 14 cleanup: collapse with MediaFileDeletionService.Execute(DeleteSeriesFilesCommand)
    // when Tv/ deletes — manga shape becomes canonical because TV is wholesale dropped.
    public class ChapterFileService : IChapterFileService,
                                      IExecute<DeleteMangaFilesCommand>,
                                      IHandleAsync<MangaDeletedEvent>
    {
        private readonly IChapterFileRepository _chapterFileRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        // Phase 11 Plan 11-06 — 5 NEW deps for IExecute<DeleteMangaFilesCommand> handler.
        // IConfigService is NOT injected: TV peer Execute body (MediaFileDeletionService.cs
        // lines 102-175) does not consume _configService; the field is only referenced in
        // the unrelated IHandle<EpisodeFileDeletedEvent> at line 218 (empty-folder cleanup,
        // out of scope for this plan).
        private readonly NzbDrone.Core.Manga.IMangaService _mangaService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly ICommandResultReporter _commandResultReporter;

        public ChapterFileService(IChapterFileRepository chapterFileRepository,
                                  IEventAggregator eventAggregator,
                                  Logger logger,
                                  NzbDrone.Core.Manga.IMangaService mangaService,
                                  IRootFolderService rootFolderService,
                                  IDiskProvider diskProvider,
                                  IRecycleBinProvider recycleBinProvider,
                                  ICommandResultReporter commandResultReporter)
        {
            _chapterFileRepository = chapterFileRepository;
            _eventAggregator = eventAggregator;
            _logger = logger;
            _mangaService = mangaService;
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _commandResultReporter = commandResultReporter;
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
        public List<string> FilterExistingFiles(List<string> files, NzbDrone.Core.Manga.Manga manga)
        {
            var mangaFiles = GetFilesByManga(manga.Id);

            return FilterExistingFiles(files, mangaFiles, manga);
        }

        // Phase 11 Plan 11-06 (sub-wave C): IExecute<DeleteMangaFilesCommand> handler —
        // manga-shape port of MediaFileDeletionService.Execute(DeleteSeriesFilesCommand)
        // body (TV peer lines 102-175). Pitfall 4 ordering — recycle FIRST (line below),
        // DB row delete SECOND (Delete call below). Per-mangaId try/catch isolates partial
        // failures; CommandResult.Indeterminate reported on each guard-clause continue.
        public void Execute(DeleteMangaFilesCommand message)
        {
            foreach (var mangaId in message.MangaIds)
            {
                try
                {
                    var manga = _mangaService.GetManga(mangaId);
                    var chapterFiles = _chapterFileRepository.GetFilesByManga(mangaId);

                    _logger.ProgressDebug("{0}: Deleting chapter files", manga.Title);

                    if (chapterFiles.Count == 0)
                    {
                        _logger.Debug("No files found for manga: {0}", manga.Title);
                        continue;
                    }

                    var rootFolder = _rootFolderService.GetBestRootFolderPath(manga.Path);

                    if (!_diskProvider.FolderExists(rootFolder))
                    {
                        _logger.Warn("Manga's root folder ({0}) doesn't exist.", rootFolder);
                        _commandResultReporter.Report(CommandResult.Indeterminate);
                        continue;
                    }

                    if (_diskProvider.GetDirectories(rootFolder).Empty())
                    {
                        _logger.Warn("Manga's root folder ({0}) is empty.", rootFolder);
                        _commandResultReporter.Report(CommandResult.Indeterminate);
                        continue;
                    }

                    foreach (var chapterFile in chapterFiles)
                    {
                        var fullPath = Path.Combine(manga.Path, chapterFile.RelativePath);

                        if (_diskProvider.FileExists(fullPath))
                        {
                            _logger.Info("Deleting chapter file: {0}", fullPath);

                            var subfolder = _diskProvider.GetParentFolder(manga.Path).GetRelativePath(_diskProvider.GetParentFolder(fullPath));

                            try
                            {
                                // ── STEP 1: recycle FIRST (Pitfall 4 — must precede DB delete)
                                _recycleBinProvider.DeleteFile(fullPath, subfolder);
                            }
                            catch (Exception e)
                            {
                                _logger.Error(e, "Unable to delete chapter file");
                                _commandResultReporter.Report(CommandResult.Indeterminate);
                                continue;
                            }

                            // ── STEP 2: DB row delete SECOND (publishes ChapterFileDeletedEvent
                            //              via existing Delete method line ~50)
                            Delete(chapterFile, DeleteMediaFileReason.Manual);
                        }
                    }

                    _logger.ProgressDebug("{0}: Deleted chapter files", manga.Title);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to delete files for manga with ID: {0}", mangaId);
                    _commandResultReporter.Report(CommandResult.Indeterminate);
                }
            }
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
        public static List<string> FilterExistingFiles(List<string> files, List<ChapterFile> mangaFiles, NzbDrone.Core.Manga.Manga manga)
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
