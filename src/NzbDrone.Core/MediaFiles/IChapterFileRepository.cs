using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Phase 9 D-09-03 #3 + 09-01 audit gap-01/gap-05: added GetFilesByMangaIds for bulk
    // paths and GetFilesWithRelativePath for collision detection symmetry with TV.
    // Role-match analog: IMediaFileRepository.
    public interface IChapterFileRepository : IBasicRepository<ChapterFile>
    {
        List<ChapterFile> GetFilesByChapter(int chapterId);
        List<ChapterFile> GetFilesByManga(int mangaId);
        List<ChapterFile> GetFilesByMangaIds(List<int> mangaIds);
        List<ChapterFile> GetFilesWithRelativePath(int mangaId, string relativePath);
        void DeleteForManga(int mangaId);
    }
}
