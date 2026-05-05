using System.Collections.Generic;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Phase 9 D-09-03 #3 + 09-01 audit: added GetFilesByMangaIds (gap-01),
    // FilterExistingFiles instance overload (gap-02), and GetFilesWithRelativePath (gap-05)
    // for accessor parity with IMediaFileService. The static FilterExistingFiles helper
    // lives on the impl class, NOT this interface (mirrors TV's MediaFileService.cs:122-132).
    // Role-match analog: IMediaFileService.
    // Consumed by Plans 06-07 ImportApprovedChapters, 06-08 lifecycle, 06-09 V5 controller,
    // 09-06 MangaDiskScanService (FilterExistingFiles is COMPILE-CRITICAL per RESEARCH §Pitfall 3).
    public interface IChapterFileService
    {
        ChapterFile Add(ChapterFile chapterFile);
        void Update(ChapterFile chapterFile);
        void Update(List<ChapterFile> chapterFiles);
        void Delete(ChapterFile chapterFile, DeleteMediaFileReason reason);
        ChapterFile Get(int id);
        List<ChapterFile> Get(IEnumerable<int> ids);
        List<ChapterFile> GetFilesByChapter(int chapterId);
        List<ChapterFile> GetFilesByManga(int mangaId);
        List<ChapterFile> GetFilesByMangaIds(List<int> mangaIds);
        List<ChapterFile> GetFilesWithRelativePath(int mangaId, string relativePath);
        List<string> FilterExistingFiles(List<string> files, Manga manga);
    }
}
