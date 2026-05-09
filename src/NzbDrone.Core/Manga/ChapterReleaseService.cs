using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling service per Phase 16 STRUCT-02 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Manga/ChapterService.cs (lines 22-298).
    // Soft-FK + service-layer cascade convention (Pitfall 2): IHandleAsync<MangaDeletedEvent>
    // resolves chapter ids via IChapterRepository.GetByMangaId then bulk-deletes releases.
    // Mirrors ChapterService.HandleAsync(MangaDeletedEvent) at ChapterService.cs:293-297.
    public class ChapterReleaseService : IChapterReleaseService,
                                          IHandleAsync<MangaDeletedEvent>
    {
        private readonly IChapterReleaseRepository _repo;
        private readonly IChapterRepository _chapterRepo;        // for DeleteByManga: resolve chapter ids → bulk delete
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public ChapterReleaseService(IChapterReleaseRepository repo,
                                     IChapterRepository chapterRepo,
                                     IEventAggregator eventAggregator,
                                     Logger logger)
        {
            _repo = repo;
            _chapterRepo = chapterRepo;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public ChapterRelease GetRelease(int id) => _repo.Get(id);

        public List<ChapterRelease> GetReleasesByChapter(int chapterId) =>
            _repo.GetByChapterId(chapterId);

        public List<ChapterRelease> GetReleasesByMangaId(int mangaId)
        {
            // N+1-safe: resolve chapter ids first, then ONE bulk SQL via GetByChapterIds.
            // Mirrors ChapterRepository.GetChaptersByMangaIds line 36-42 pattern.
            var chapterIds = _chapterRepo.GetByMangaId(mangaId).Select(c => c.Id).ToList();
            if (chapterIds.Count == 0)
            {
                return new List<ChapterRelease>();
            }

            return _repo.GetByChapterIds(chapterIds);
        }

        // Sonarr divergence: Phase 16 STRUCT-06 — Wanted/Missing languages[] filter consumes this
        // when the page spans multiple manga (mangaIds filter unset). Single bulk SQL via the
        // repo's GetByChapterIds. Empty input short-circuits to empty list (mirrors GetReleasesByMangaId).
        public List<ChapterRelease> GetReleasesByChapterIds(List<int> chapterIds)
        {
            if (chapterIds == null || chapterIds.Count == 0)
            {
                return new List<ChapterRelease>();
            }

            return _repo.GetByChapterIds(chapterIds);
        }

        public void Insert(ChapterRelease release) => _repo.Insert(release);
        public void InsertMany(List<ChapterRelease> releases) => _repo.InsertMany(releases);
        public void Update(ChapterRelease release) => _repo.Update(release);
        public void UpdateMany(List<ChapterRelease> releases) => _repo.UpdateMany(releases);

        public void DeleteByManga(int mangaId)
        {
            // Public surface for the cascade. Used both by IHandleAsync and by direct service callers.
            var chapterIds = _chapterRepo.GetByMangaId(mangaId).Select(c => c.Id).ToList();
            if (chapterIds.Count == 0)
            {
                return;
            }

            var releases = _repo.GetByChapterIds(chapterIds);
            _repo.DeleteMany(releases);
        }

        // Phase 16 STRUCT-02 cascade — sibling of TV's EpisodeService.HandleAsync(SeriesDeletedEvent) +
        // Mangarr's existing ChapterService.HandleAsync(MangaDeletedEvent) at ChapterService.cs:293-297.
        // Required because 001_mangarr_baseline.cs uses zero hard SQL FKs (Pitfall 2) — soft FK +
        // service-layer cascade is the established convention.
        public void HandleAsync(MangaDeletedEvent message)
        {
            DeleteByManga(message.Manga.Id);
        }
    }
}
