using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: MediaFileRepository.
    public class ChapterFileRepository : BasicRepository<ChapterFile>, IChapterFileRepository
    {
        public ChapterFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<ChapterFile> GetFilesByChapter(int chapterId)
        {
            return Query(c => c.ChapterId == chapterId).ToList();
        }

        public List<ChapterFile> GetFilesByManga(int mangaId)
        {
            return Query(c => c.MangaId == mangaId).ToList();
        }

        // Phase 9 D-09-03 #3 + 09-01 audit gap-01: bulk get-by-multiple-parent-IDs.
        // Mirrors MediaFileRepository.GetFilesBySeriesIds verbatim with manga substitution.
        public List<ChapterFile> GetFilesByMangaIds(List<int> mangaIds)
        {
            return Query(c => mangaIds.Contains(c.MangaId)).ToList();
        }

        // Phase 9 D-09-03 #3 + 09-01 audit gap-05: relative-path collision detection.
        // Mirrors MediaFileRepository.GetFilesWithRelativePath verbatim with manga substitution.
        public List<ChapterFile> GetFilesWithRelativePath(int mangaId, string relativePath)
        {
            return Query(c => c.MangaId == mangaId && c.RelativePath == relativePath).ToList();
        }

        public void DeleteForManga(int mangaId)
        {
            Delete(c => c.MangaId == mangaId);
        }
    }
}
