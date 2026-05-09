using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling repo interface per Phase 16 STRUCT-02 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Manga/IChapterRepository.cs.
    // GetByMangaId / GetByChapterIds bulk methods exist to support N+1 avoidance in
    // Mangarr.Api.V5/Manga/Chapter/ChapterController.GetChapters (per RESEARCH §Pitfall:
    // Adding releases via N+1 queries).
    public interface IChapterReleaseRepository : IBasicRepository<ChapterRelease>
    {
        // Natural-key Find — mirrors ChapterRepository.Find(int, decimal, string) lines 23-29.
        // SingleOrDefault enforces "the UNIQUE-(ChapterId, Lang, Group) constraint means at most one match".
        ChapterRelease Find(int chapterId, string translatedLanguage, string scanlationGroup);

        // Per-Chapter retrieval — mirrors ChapterRepository.GetByMangaId line 31-34.
        List<ChapterRelease> GetByChapterId(int chapterId);

        // Bulk-by-many-parents — mirrors ChapterRepository.GetChaptersByMangaIds line 36-42 verbatim.
        List<ChapterRelease> GetByChapterIds(List<int> chapterIds);

        // Per-Manga bulk lookup — required by ChapterController.GetChapters(mangaId) for N+1 avoidance.
        // Repository implementation is intentionally not-supported here (BasicRepository purity);
        // the SERVICE layer (ChapterReleaseService.GetReleasesByMangaId) does the IChapterRepository
        // join + GetByChapterIds bulk pattern.
        List<ChapterRelease> GetByMangaId(int mangaId);
    }
}
