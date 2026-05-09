// Sonarr divergence: NEW manga sibling test fixture per Phase 16 STRUCT-02 — see DIVERGENCE.md.
// Role-match analog: src/NzbDrone.Core.Test/Manga/ChapterServiceFixture.cs.
// Asserts the soft-FK + service-cascade convention (Pitfall 2): MangaDeletedEvent fan-out
// goes IChapterRepository.GetByMangaId -> IChapterReleaseRepository.GetByChapterIds -> DeleteMany,
// matching ChapterService.HandleAsync(MangaDeletedEvent) at ChapterService.cs:293-297.
// Plan 16-02 (Wave 1) landed the production ChapterReleaseService + IChapterReleaseRepository
// types; the Wave-0 `#if PHASE_16_WAVE_1` guard was removed inline (Plan 16-01 hand-off
// option (b)).
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    [TestFixture]
    public class ChapterReleaseServiceFixture
        : CoreTest<NzbDrone.Core.Manga.ChapterReleaseService>
    {
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
        public void GetReleasesByChapterIds_delegates_to_repo_bulk()
        {
            // Sonarr divergence: Phase 16 STRUCT-06 — N+1-safe bulk path consumed by
            // MangaMissingController's languages[] filter. Single repo call; empty input
            // short-circuits to empty list (mirrors GetReleasesByMangaId guard).
            var ids = new List<int> { 1, 2, 3 };
            var releases = new List<NzbDrone.Core.Manga.ChapterRelease>
            {
                new() { Id = 10, ChapterId = 1, TranslatedLanguage = "en" },
                new() { Id = 11, ChapterId = 2, TranslatedLanguage = "es" },
            };
            Mocker.GetMock<NzbDrone.Core.Manga.IChapterReleaseRepository>()
                .Setup(r => r.GetByChapterIds(ids)).Returns(releases);

            Subject.GetReleasesByChapterIds(ids).Should().BeEquivalentTo(releases);
        }

        [Test]
        public void GetReleasesByChapterIds_with_empty_input_short_circuits_without_repo_call()
        {
            // Empty-input guard: avoid an unnecessary SQL roundtrip when the page is empty.
            Subject.GetReleasesByChapterIds(new List<int>()).Should().BeEmpty();

            Mocker.GetMock<NzbDrone.Core.Manga.IChapterReleaseRepository>()
                .Verify(r => r.GetByChapterIds(It.IsAny<List<int>>()), Times.Never);
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

            // MangaDeletedEvent ctor is 2-arg in this repo (Manga, deleteFiles); the
            // 3rd-arg `addImportListExclusion` referenced in the Wave-0 stub does not
            // exist — Plan 16-02 hand-off correction.
            Subject.HandleAsync(new NzbDrone.Core.Manga.Events.MangaDeletedEvent(
                new NzbDrone.Core.Manga.Manga { Id = 99 },
                deleteFiles: false));

            Mocker.GetMock<NzbDrone.Core.Manga.IChapterReleaseRepository>()
                .Verify(r => r.DeleteMany(It.IsAny<List<NzbDrone.Core.Manga.ChapterRelease>>()), Times.Once);
        }
    }
}
