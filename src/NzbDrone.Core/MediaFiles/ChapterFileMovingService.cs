using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;

// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — MediaFiles/EpisodeImport/ DELETED.
//   using NzbDrone.Core.MediaFiles.EpisodeImport; ← deleted
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.RootFolders;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit (no-sibling/EpisodeFileMovingService).
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeFileMovingService.cs (TV).
    //
    // Mirrors EpisodeFileMovingService's three-method surface — Move(ChapterFile, Manga) for the
    // rename pipeline, Move(ChapterFile, LocalChapter) for the import pipeline, and Copy(ChapterFile,
    // LocalChapter) for the hardlink-or-copy import mode (honors _configService.CopyUsingHardlinks).
    // Publishes ChapterFolderCreatedEvent when EnsureChapterFolder materializes a fresh manga or
    // chapter directory — mirroring TV's EpisodeFolderCreatedEvent publish site.
    //
    // Slim ctor relative to TV: IChapterService is unused (TV's _episodeService.GetEpisodesByFileId
    // hydrates the multi-episode list inside Move(ef, series); manga has 1:1 ChapterFile→Chapter so
    // the caller passes the Chapter list via Manga.Path-only resolution and Plan 02-12's
    // IUpdateChapterFileService takes the Chapter list directly). SeasonFolder branch is dropped
    // per D-13 (manga has no season concept).
    //
    // Phase 8 Plan 99-09: IImportChapterScript hooked into the LocalChapter import paths
    // (Move + Copy). Rename pipeline (MoveChapterFile(ChapterFile, Manga)) does NOT call the
    // script — TV's analog doesn't either.
    //
    // Phase 14 cleanup: collapse with EpisodeFileMovingService when Tv/ deletes.
    public interface IMoveChapterFiles
    {
        ChapterFile MoveChapterFile(ChapterFile chapterFile, MangaModel manga);
        ChapterFile MoveChapterFile(ChapterFile chapterFile, LocalChapter localChapter);
        ChapterFile CopyChapterFile(ChapterFile chapterFile, LocalChapter localChapter);
    }

    public class ChapterFileMovingService : IMoveChapterFiles
    {
        private readonly IUpdateChapterFileService _updateChapterFileService;
        private readonly IBuildMangaFileNames _buildFileNames;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IDiskProvider _diskProvider;
        private readonly IMediaFileAttributeService _mediaFileAttributeService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IConfigService _configService;
        private readonly IImportChapterScript _scriptImportDecider;
        private readonly Logger _logger;

        public ChapterFileMovingService(IUpdateChapterFileService updateChapterFileService,
                                        IBuildMangaFileNames buildFileNames,
                                        IDiskTransferService diskTransferService,
                                        IDiskProvider diskProvider,
                                        IMediaFileAttributeService mediaFileAttributeService,
                                        IRootFolderService rootFolderService,
                                        IEventAggregator eventAggregator,
                                        IConfigService configService,
                                        IImportChapterScript scriptImportDecider,
                                        Logger logger)
        {
            _updateChapterFileService = updateChapterFileService;
            _buildFileNames = buildFileNames;
            _diskTransferService = diskTransferService;
            _diskProvider = diskProvider;
            _mediaFileAttributeService = mediaFileAttributeService;
            _rootFolderService = rootFolderService;
            _eventAggregator = eventAggregator;
            _configService = configService;
            _scriptImportDecider = scriptImportDecider;
            _logger = logger;
        }

        public ChapterFile MoveChapterFile(ChapterFile chapterFile, MangaModel manga)
        {
            // Manga is 1:1 ChapterFile→Chapter — caller (RenameChapterFileService, future Plan)
            // hydrates a single-element Chapter list via IChapterService. The moving service stays
            // free of IChapterService so it doesn't drag the chapter-by-file lookup into rename
            // flows that already have the Chapter aggregate.
            var chapters = new List<Manga.Chapter>();

            var filePath = _buildFileNames.BuildFilePath(chapters, manga, release: null, extension: Path.GetExtension(chapterFile.RelativePath));

            EnsureChapterFolder(chapterFile, manga, filePath);

            _logger.Debug("Renaming chapter file: {0} to {1}", chapterFile, filePath);

            return TransferFile(chapterFile, manga, chapters, filePath, TransferMode.Move);
        }

        public ChapterFile MoveChapterFile(ChapterFile chapterFile, LocalChapter localChapter)
        {
            var filePath = _buildFileNames.BuildFilePath(localChapter.Chapters, localChapter.Manga, localChapter.Release, Path.GetExtension(localChapter.Path), null, localChapter.CustomFormats);

            EnsureChapterFolder(chapterFile, localChapter.Manga, filePath);

            _logger.Debug("Moving chapter file: {0} to {1}", chapterFile.Path, filePath);

            return RunScriptThenTransfer(chapterFile, localChapter, filePath, TransferMode.Move);
        }

        public ChapterFile CopyChapterFile(ChapterFile chapterFile, LocalChapter localChapter)
        {
            var filePath = _buildFileNames.BuildFilePath(localChapter.Chapters, localChapter.Manga, localChapter.Release, Path.GetExtension(localChapter.Path), null, localChapter.CustomFormats);

            EnsureChapterFolder(chapterFile, localChapter.Manga, filePath);

            if (_configService.CopyUsingHardlinks)
            {
                _logger.Debug("Attempting to hardlink chapter file: {0} to {1}", chapterFile.Path, filePath);
                return RunScriptThenTransfer(chapterFile, localChapter, filePath, TransferMode.HardLinkOrCopy);
            }

            _logger.Debug("Copying chapter file: {0} to {1}", chapterFile.Path, filePath);
            return RunScriptThenTransfer(chapterFile, localChapter, filePath, TransferMode.Copy);
        }

        // Phase 8 Plan 99-09 — gives the user-configured import script first crack at the move.
        // If the script reports MoveComplete, we trust it and skip our internal TransferFile.
        // DeferMove (default when UseScriptImport=false) and RenameRequested fall through to
        // the normal internal transfer path.
        private ChapterFile RunScriptThenTransfer(ChapterFile chapterFile, LocalChapter localChapter, string destinationFilePath, TransferMode mode)
        {
            var sourcePath = chapterFile.Path ?? Path.Combine(localChapter.Manga.Path, chapterFile.RelativePath);
            var decision = _scriptImportDecider.TryImport(sourcePath, destinationFilePath, localChapter, chapterFile, mode);

            if (decision == ScriptImportDecision.MoveComplete && localChapter.ScriptImported)
            {
                // Script reported completion + acknowledged the move. Skip internal transfer.
                _logger.Debug("Import script reported MoveComplete for {0}; skipping internal transfer", chapterFile.Path);
                return chapterFile;
            }

            return TransferFile(chapterFile, localChapter.Manga, localChapter.Chapters, destinationFilePath, mode);
        }

        private ChapterFile TransferFile(ChapterFile chapterFile, MangaModel manga, List<Manga.Chapter> chapters, string destinationFilePath, TransferMode mode)
        {
            Ensure.That(chapterFile, () => chapterFile).IsNotNull();
            Ensure.That(manga, () => manga).IsNotNull();
            Ensure.That(destinationFilePath, () => destinationFilePath).IsValidPath(PathValidationType.CurrentOs);

            var chapterFilePath = chapterFile.Path ?? Path.Combine(manga.Path, chapterFile.RelativePath);

            if (!_diskProvider.FileExists(chapterFilePath))
            {
                throw new FileNotFoundException("Chapter file path does not exist", chapterFilePath);
            }

            if (chapterFilePath == destinationFilePath)
            {
                throw new SameFilenameException("File not moved, source and destination are the same", chapterFilePath);
            }

            chapterFile.RelativePath = manga.Path.GetRelativePath(destinationFilePath);

            _diskTransferService.TransferFile(chapterFilePath, destinationFilePath, mode);

            _updateChapterFileService.ChangeFileDateForFile(chapterFile, manga, chapters);

            try
            {
                _mediaFileAttributeService.SetFolderLastWriteTime(manga.Path, chapterFile.DateAdded);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to set last write time");
            }

            _mediaFileAttributeService.SetFilePermissions(destinationFilePath);

            return chapterFile;
        }

        private void EnsureChapterFolder(ChapterFile chapterFile, MangaModel manga, string filePath)
        {
            var chapterFolder = Path.GetDirectoryName(filePath);
            var mangaFolder = manga.Path;
            var rootFolder = _rootFolderService.GetBestRootFolderPath(mangaFolder);

            if (rootFolder.IsNullOrWhiteSpace())
            {
                throw new RootFolderNotFoundException($"Root folder was not found, '{mangaFolder}' is not a subdirectory of a defined root folder.");
            }

            if (!_diskProvider.FolderExists(rootFolder))
            {
                throw new RootFolderNotFoundException($"Root folder '{rootFolder}' was not found.");
            }

            var changed = false;
            var newEvent = new ChapterFolderCreatedEvent(chapterFile);

            if (!_diskProvider.FolderExists(mangaFolder))
            {
                CreateFolder(mangaFolder);
                newEvent.MangaFolder = mangaFolder;
                changed = true;
            }

            // Manga has no Season concept (D-13) — mangaFolder→chapterFolder is the only nesting.
            if (mangaFolder != chapterFolder && !_diskProvider.FolderExists(chapterFolder))
            {
                CreateFolder(chapterFolder);
                newEvent.ChapterFolder = chapterFolder;
                changed = true;
            }

            if (changed)
            {
                _eventAggregator.PublishEvent(newEvent);
            }
        }

        private void CreateFolder(string directoryName)
        {
            Ensure.That(directoryName, () => directoryName).IsNotNullOrWhiteSpace();

            var parentFolder = new OsPath(directoryName).Directory.FullPath;
            if (!_diskProvider.FolderExists(parentFolder))
            {
                CreateFolder(parentFolder);
            }

            try
            {
                _diskProvider.CreateFolder(directoryName);
            }
            catch (IOException ex)
            {
                _logger.Error(ex, "Unable to create directory: {0}", directoryName);
            }

            _mediaFileAttributeService.SetFolderPermissions(directoryName);
        }
    }
}
