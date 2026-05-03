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

        public void DeleteForManga(int mangaId)
        {
            Delete(c => c.MangaId == mangaId);
        }
    }
}
