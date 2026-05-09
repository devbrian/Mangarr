using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling repo per Phase 16 STRUCT-02 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Manga/ChapterRepository.cs.
    // Soft-FK convention (Pitfall 2): ChapterId is a plain int column; cascade-delete
    // lives in C# at IChapterReleaseService.HandleAsync(MangaDeletedEvent), NOT here.
    public class ChapterReleaseRepository : BasicRepository<ChapterRelease>, IChapterReleaseRepository
    {
        private readonly Logger _logger;

        public ChapterReleaseRepository(IMainDatabase database, IEventAggregator eventAggregator, Logger logger)
            : base(database, eventAggregator)
        {
            _logger = logger;
        }

        public ChapterRelease Find(int chapterId, string translatedLanguage, string scanlationGroup)
        {
            return Query(r => r.ChapterId == chapterId
                              && r.TranslatedLanguage == translatedLanguage
                              && r.ScanlationGroup == scanlationGroup)
                .SingleOrDefault();
        }

        public List<ChapterRelease> GetByChapterId(int chapterId)
        {
            return Query(r => r.ChapterId == chapterId).ToList();
        }

        public List<ChapterRelease> GetByChapterIds(List<int> chapterIds)
        {
            if (chapterIds == null || chapterIds.Count == 0)
            {
                return new List<ChapterRelease>();
            }

            return Query(r => chapterIds.Contains(r.ChapterId)).ToList();
        }

        public List<ChapterRelease> GetByMangaId(int mangaId)
        {
            // N+1-safe: this method intentionally throws here. The repo layer is per-table
            // only — to resolve a Manga -> chapter ids -> releases join we'd need to inject
            // IChapterRepository, but BasicRepository's constructor signature only allows
            // (IMainDatabase, IEventAggregator). To preserve repo purity we throw here and
            // the SERVICE layer (ChapterReleaseService.GetReleasesByMangaId) does the
            // IChapterRepository.GetByMangaId(mangaId) -> GetByChapterIds([...]) bulk
            // N+1-safe join (mirrors ChapterRepository.GetChaptersByMangaIds line 36-42 pattern).
            throw new System.NotSupportedException(
                "Use IChapterReleaseService.GetReleasesByMangaId — repo is per-table only; "
                + "service layer does the IChapterRepository.GetByMangaId(mangaId) -> "
                + "GetByChapterIds([...]) bulk N+1-safe join.");
        }
    }
}
