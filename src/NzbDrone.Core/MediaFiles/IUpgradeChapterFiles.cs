using NzbDrone.Core.MediaFiles.MangaImport;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-05 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs (IUpgradeMediaFiles).
    // Phase 14 cleanup: collapse with IUpgradeMediaFiles when Tv/ deletes.
    public interface IUpgradeChapterFiles
    {
        ChapterFileMoveResult UpgradeChapterFile(ChapterFile chapterFile, LocalChapter localChapter, bool copyOnly = false);
    }
}
