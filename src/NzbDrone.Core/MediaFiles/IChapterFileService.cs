using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: IMediaFileService.
    // Consumed by Plans 06-07 ImportApprovedChapters, 06-08 lifecycle, 06-09 V5 controller.
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
    }
}
