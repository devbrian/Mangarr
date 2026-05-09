using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling service interface per Phase 16 STRUCT-02 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Manga/IChapterService.cs.
    // Surface intentionally narrower than IChapterService — no IHandle<ChapterFileAddedEvent> etc.
    // because files attach to canonical Chapters, not to ChapterReleases (per STRUCT-04).
    public interface IChapterReleaseService
    {
        ChapterRelease GetRelease(int id);
        List<ChapterRelease> GetReleasesByChapter(int chapterId);
        List<ChapterRelease> GetReleasesByMangaId(int mangaId);    // STRUCT-08 N+1 avoidance — controller bulk-load path
        void Insert(ChapterRelease release);
        void InsertMany(List<ChapterRelease> releases);
        void Update(ChapterRelease release);
        void UpdateMany(List<ChapterRelease> releases);
        void DeleteByManga(int mangaId);                            // public surface for cascade handler
    }
}
