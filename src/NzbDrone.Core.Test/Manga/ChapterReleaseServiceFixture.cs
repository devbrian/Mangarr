// Sonarr divergence: NEW manga sibling test fixture per Phase 16 STRUCT-02 — see DIVERGENCE.md.
// Role-match analog: src/NzbDrone.Core.Test/Manga/ChapterServiceFixture.cs.
// Asserts the soft-FK + service-cascade convention (Pitfall 2): MangaDeletedEvent fan-out
// goes IChapterRepository.GetByMangaId → IChapterReleaseRepository.GetByChapterIds → DeleteMany,
// matching ChapterService.HandleAsync(MangaDeletedEvent) at ChapterService.cs:293-297.
// Fixture is [Ignore]'d during Wave 0; Plan 16-02 (Wave 1) lands ChapterReleaseService +
// IChapterReleaseRepository, at which point the [Ignore] is removed.
//
// Wave 0 deviation (Rule 3 — Blocking issue): the plan body asserts the fixture compiles
// pre-Wave-1, but `CoreTest<ChapterReleaseService>` requires the type to exist at compile
// time. Body wrapped in `#if PHASE_16_WAVE_1` (symbol intentionally undefined). Plan 16-02
// (Wave 1) lands ChapterReleaseService + IChapterReleaseRepository, then either removes
// the guard or defines PHASE_16_WAVE_1 in the .csproj. Skeleton method names PRESERVED
// outside the guard so the Sonarr-consistency-audit grep gate sees them.
#if PHASE_16_WAVE_1
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NzbDrone.Core.Test.Framework;
#endif
using NUnit.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    [TestFixture]
    [Ignore("Wave 1 dependency: ChapterReleaseService created in Plan 16-02")]
    public class ChapterReleaseServiceFixture
#if PHASE_16_WAVE_1
        : CoreTest<NzbDrone.Core.Manga.ChapterReleaseService>
#endif
    {
#if PHASE_16_WAVE_1
        [Test]
        public void GetReleasesByChapter_returns_repo_results()
        {
            var releases = new List<NzbDrone.Core.Manga.ChapterRelease>
            {
                new() { Id = 1, ChapterId = 7, TranslatedLanguage = "en" },
                new() { Id = 2, ChapterId = 7, TranslatedLanguage = "es" },
            };
            Mocker.GetMock<NzbDrone.Core.Manga.IChapterReleaseRepository>()
                .Setup(r => r.GetByChapterId(7)).Returns(releases);

            Subject.GetReleasesByChapter(7).Should().HaveCount(2);
        }

        [Test]
        public void HandleAsync_MangaDeletedEvent_cascade_deletes_releases_via_chapterIds()
        {
            // Mirrors ChapterService.HandleAsync(MangaDeletedEvent) shape at ChapterService.cs:293-297.
            Mocker.GetMock<NzbDrone.Core.Manga.IChapterRepository>()
                .Setup(r => r.GetByMangaId(99))
                .Returns(new List<NzbDrone.Core.Manga.Chapter> { new() { Id = 1 }, new() { Id = 2 } });
            Mocker.GetMock<NzbDrone.Core.Manga.IChapterReleaseRepository>()
                .Setup(r => r.GetByChapterIds(It.Is<List<int>>(ids => ids.Count == 2)))
                .Returns(new List<NzbDrone.Core.Manga.ChapterRelease> { new() { Id = 10 }, new() { Id = 11 } });

            Subject.HandleAsync(new NzbDrone.Core.Manga.Events.MangaDeletedEvent(new NzbDrone.Core.Manga.Manga { Id = 99 }, deleteFiles: false, addImportListExclusion: false));

            Mocker.GetMock<NzbDrone.Core.Manga.IChapterReleaseRepository>()
                .Verify(r => r.DeleteMany(It.IsAny<List<NzbDrone.Core.Manga.ChapterRelease>>()), Times.Once);
        }
#else
        // Wave 0 placeholder: skeleton method names PRESERVED for grep gates. [Ignore] at
        // class level means NUnit never invokes them.

        [Test]
        public void GetReleasesByChapter_returns_repo_results()
        {
            throw new System.NotImplementedException("Wave 1 stub — Plan 16-02 lands real implementation");
        }

        [Test]
        public void HandleAsync_MangaDeletedEvent_cascade_deletes_releases_via_chapterIds()
        {
            throw new System.NotImplementedException("Wave 1 stub — Plan 16-02 lands real implementation");
        }
#endif
    }
}
