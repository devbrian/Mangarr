using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;

// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — MediaFiles/EpisodeImport/ DELETED.
//   using NzbDrone.Core.MediaFiles.EpisodeImport; ← deleted
using NzbDrone.Core.MediaFiles.MangaImport;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-05 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs.
    //
    // Backfill for the D-10 three-state upgrade-allowed gate's missing side effect.
    // Phase 6 ImportApprovedChapters enforces the upgrade decision but does NOT recycle
    // the previous CBZ — every successful upgrade silently leaked the old file. This
    // service wires the existing media-agnostic IRecycleBinProvider into the upgrade
    // promotion site (D-09-03 process-coupled rule applied to D-09-05 backfill: service
    // + insertion site ship atomically — paired with the ImportApprovedChapters insert).
    //
    // CRITICAL ORDERING (RESEARCH §Threat 535 / PATTERNS §A — mirrors TV
    // UpgradeMediaFileService.cs:65 then :73 verbatim): _recycleBinProvider.DeleteFile
    // MUST run BEFORE _chapterFileService.Delete. The inverse leaks the file path on
    // disk if the recycle-bin step throws.
    //
    // Manga divergences from TV UpgradeMediaFileService:
    //   * LocalChapter has a SINGLE Chapter (not a List<Episode>); manga is one-CBZ-per-
    //     chapter so the multi-episode-file iteration shape collapses to a single
    //     existingFile lookup via IChapterFileService.Get(ChapterFileId.Value).
    //   * Chapter.ChapterFileId is int? (nullable) vs Episode.EpisodeFileId int sentinel
    //     0; null OR <= 0 means "no existing file — skip recycle".
    //   * IMoveChapterFiles surface differs: MoveChapterFile(file, localChapter) /
    //     CopyChapterFile(file, localChapter) (no separate Move/Copy distinction by name
    //     parameter — copyOnly switches between methods).
    //
    // Phase 14 cleanup: collapse with UpgradeMediaFileService when Tv/ deletes.
    public class UpgradeChapterFileService : IUpgradeChapterFiles
    {
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IChapterFileService _chapterFileService;
        private readonly IMoveChapterFiles _chapterFileMover;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public UpgradeChapterFileService(IRecycleBinProvider recycleBinProvider,
                                         IChapterFileService chapterFileService,
                                         IMoveChapterFiles chapterFileMover,
                                         IDiskProvider diskProvider,
                                         Logger logger)
        {
            _recycleBinProvider = recycleBinProvider;
            _chapterFileService = chapterFileService;
            _chapterFileMover = chapterFileMover;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public ChapterFileMoveResult UpgradeChapterFile(ChapterFile chapterFile, LocalChapter localChapter, bool copyOnly = false)
        {
            var moveFileResult = new ChapterFileMoveResult();

            // Manga is one-CBZ-per-chapter — the existing file (if any) is the chapter's
            // current ChapterFile. Mirrors TV's existingFiles enumeration adapted to the
            // singular shape (Chapter.ChapterFileId is nullable; null OR <= 0 means none).
            var existingFile = localChapter.Chapter != null
                               && localChapter.Chapter.ChapterFileId.HasValue
                               && localChapter.Chapter.ChapterFileId.Value > 0
                ? _chapterFileService.Get(localChapter.Chapter.ChapterFileId.Value)
                : null;

            var rootFolder = _diskProvider.GetParentFolder(localChapter.Manga.Path);

            // If there is an existing chapter file and the root folder is missing, throw, so the old file isn't left behind during the import process.
            if (existingFile != null && !_diskProvider.FolderExists(rootFolder))
            {
                throw new RootFolderNotFoundException($"Root folder '{rootFolder}' was not found.");
            }

            if (existingFile != null)
            {
                var chapterFilePath = Path.Combine(localChapter.Manga.Path, existingFile.RelativePath);
                var subfolder = rootFolder.GetRelativePath(_diskProvider.GetParentFolder(chapterFilePath));
                string recycleBinPath = null;

                if (_diskProvider.FileExists(chapterFilePath))
                {
                    _logger.Debug("Removing existing chapter file: {0}", existingFile);
                    recycleBinPath = _recycleBinProvider.DeleteFile(chapterFilePath, subfolder); // ← RECYCLE FIRST (PATTERNS §A)
                }
                else
                {
                    _logger.Warn("Existing chapter file missing from disk: {0}", chapterFilePath);
                }

                moveFileResult.OldFiles.Add(new DeletedChapterFile(existingFile, recycleBinPath));
                _chapterFileService.Delete(existingFile, DeleteMediaFileReason.Upgrade);          // ← DELETE ROW SECOND (PATTERNS §A)
            }

            // Move/copy the new file to its destination via the existing IMoveChapterFiles
            // (Phase 8 cluster-02 ChapterFileMovingService). copyOnly switches between
            // MoveChapterFile (TransferMode.Move) and CopyChapterFile (Copy / HardLinkOrCopy).
            //
            // chapterFile == null is a recycle-only mode used by Phase 6 ImportApprovedChapters
            // step 0.5 (D-09-05): the caller already owns the new-file move via _diskProvider.MoveFile
            // and only needs the recycle+delete-row side effect documented above. Pitfall 4 ordering
            // is preserved either way — recycle + DB delete-row run BEFORE the caller's move + Add +
            // ChapterImportedEvent publish chain.
            if (chapterFile != null)
            {
                if (copyOnly)
                {
                    moveFileResult.ChapterFile = _chapterFileMover.CopyChapterFile(chapterFile, localChapter);
                }
                else
                {
                    moveFileResult.ChapterFile = _chapterFileMover.MoveChapterFile(chapterFile, localChapter);
                }
            }

            return moveFileResult;
        }
    }
}
