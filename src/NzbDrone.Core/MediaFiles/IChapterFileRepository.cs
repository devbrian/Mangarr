using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: IMediaFileRepository.
    public interface IChapterFileRepository : IBasicRepository<ChapterFile>
    {
        List<ChapterFile> GetFilesByChapter(int chapterId);
        List<ChapterFile> GetFilesByManga(int mangaId);
        void DeleteForManga(int mangaId);
    }
}
