using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Test.Common;
using Mangarr.Api.V5.Manga.Chapter;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace NzbDrone.Api.Test.Manga.Chapter
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 13 Plan 13-06 (Wave 4 —
    // D-13-04 forward-prophylactic + D-13-07 Series-rename-family rule). Authored alongside
    // the controller per VALIDATION.md Wave 0 fixture discipline.
    //
    // Role-match analog: TV-side has no RenameEpisodeControllerFixture (Sonarr never
    // shipped one); this fixture follows the same AutoMoqer + TestBase<T> pattern that
    // MangaCutoffControllerFixture / ChapterControllerFixture established for
    // Phase 6 + Phase 12 V5 controller fixtures.
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test
    // does not project-reference Mangarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // every other src/NzbDrone.Api.Test/Manga/**/*Fixture.cs.
    //
    // Per-plan unit-test filter (D-13-12-style): dotnet test --filter
    // "FullyQualifiedName~RenameChapterController" must return >=1 passing test. This
    // fixture provides:
    //   1. Reflective Attribute lookup confirms route literal "manga/rename" — the Plan
    //      13-06 / D-13-07 contract that the frontend useOrganizePreview.ts:23 fetch path
    //      depends on (currently calling /rename — a manga peer at /manga/rename is
    //      forward-prophylactic for the v1 frontend rebrand).
    //   2. Base-class assertion: RenameChapterController extends bare Controller (NOT
    //      RestControllerWithSignalR — rename preview is read-only on-demand, no SignalR
    //      contract). Mirror of TV peer RenameEpisodeController.cs:11.
    //   3. GetChapters with chapterNumber: 2-arg IRenameChapterFileService overload is
    //      invoked (decimal — Phase 2 D-12).
    //   4. GetChapters without chapterNumber: 1-arg overload is invoked.
    //   5. Bulk GetChapters with empty mangaIds: BadRequestException thrown (T-13-05
    //      mitigation).
    //   6. Bulk GetChapters with non-positive mangaId: BadRequestException thrown (T-13-05
    //      mitigation).
    [TestFixture]
    public class RenameChapterControllerFixture : TestBase<RenameChapterController>
    {
        [Test]
        public void Route_attribute_is_manga_rename_literal_per_plan_07_02_contract()
        {
            // Plan 13-06 / D-13-07 Series-rename-family contract — the route attribute string
            // is the load-bearing contract between the frontend organize-preview hook (Plan 13-06
            // forward-prophylactic per D-13-04 — current TV-only useOrganizePreview.ts:23
            // calls `/rename`; manga peer needed for v1) and this controller. Mismatched
            // route literal silently breaks the fetch path.
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(RenameChapterController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr!.Resource.Should().Be("manga/rename");
        }

        [Test]
        public void Controller_extends_bare_Controller_not_RestController()
        {
            // Mirror of TV peer RenameEpisodeController.cs:11 — bare Controller base, NOT
            // RestControllerWithSignalR. Rename preview is a read-only on-demand endpoint
            // with no SignalR push contract (no chapter-state mutation; no React Query cache
            // to invalidate on mutation). A future regression to RestControllerWithSignalR
            // would force a TResource/TModel pair that doesn't fit the rename-preview shape.
            typeof(RenameChapterController).BaseType.Should().Be(typeof(Controller),
                "RenameChapterController must extend bare Controller — mirror of TV peer " +
                "RenameEpisodeController; rename preview has no SignalR contract");
        }

        [Test]
        public void GetChapters_with_chapterNumber_calls_2arg_overload()
        {
            // Phase 2 D-12 widen: chapterNumber is decimal? not int? — exercise the
            // 2-arg overload IRenameChapterFileService.GetRenamePreviews(int, decimal?).
            var previews = new List<RenameChapterFilePreview>
            {
                new()
                {
                    MangaId = 42,
                    ChapterIds = new List<int> { 7 },
                    ChapterNumbers = new List<decimal> { 1.5m },
                    ChapterFileId = 99,
                    ExistingPath = "old.cbz",
                    NewPath = "new.cbz",
                },
            };

            Mocker.GetMock<IRenameChapterFileService>()
                  .Setup(s => s.GetRenamePreviews(42, (decimal?)1.5m))
                  .Returns(previews);

            var result = Subject.GetChapters(42, 1.5m);

            result.Value.Should().NotBeNull();
            result.Value!.Should().HaveCount(1);
            result.Value[0].ChapterFileId.Should().Be(99);
            result.Value[0].ChapterNumbers.Should().BeEquivalentTo(new[] { 1.5m });

            Mocker.GetMock<IRenameChapterFileService>()
                  .Verify(s => s.GetRenamePreviews(42, (decimal?)1.5m), Times.Once);
            Mocker.GetMock<IRenameChapterFileService>()
                  .Verify(s => s.GetRenamePreviews(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void GetChapters_without_chapterNumber_calls_1arg_overload()
        {
            // No chapterNumber → 1-arg overload IRenameChapterFileService.GetRenamePreviews(int).
            var previews = new List<RenameChapterFilePreview>
            {
                new()
                {
                    MangaId = 42,
                    ChapterIds = new List<int> { 1, 2 },
                    ChapterNumbers = new List<decimal> { 1m, 2m },
                    ChapterFileId = 100,
                    ExistingPath = "a.cbz",
                    NewPath = "b.cbz",
                },
            };

            Mocker.GetMock<IRenameChapterFileService>()
                  .Setup(s => s.GetRenamePreviews(42))
                  .Returns(previews);

            var result = Subject.GetChapters(42, null);

            result.Value.Should().NotBeNull();
            result.Value!.Should().HaveCount(1);
            result.Value[0].MangaId.Should().Be(42);

            Mocker.GetMock<IRenameChapterFileService>()
                  .Verify(s => s.GetRenamePreviews(42), Times.Once);
            Mocker.GetMock<IRenameChapterFileService>()
                  .Verify(s => s.GetRenamePreviews(It.IsAny<int>(), It.IsAny<decimal?>()), Times.Never);
        }

        [Test]
        public void GetChapters_bulk_with_empty_mangaIds_throws_BadRequestException()
        {
            // T-13-05 mitigation: bulk endpoint must reject empty input — mirrors TV peer's
            // seriesIds non-empty check at RenameEpisodeController.cs:36-39.
            Assert.Throws<BadRequestException>(() => Subject.GetChapters(new List<int>()));

            Mocker.GetMock<IRenameChapterFileService>()
                  .Verify(s => s.GetRenamePreviews(It.IsAny<List<int>>()), Times.Never);
        }

        [Test]
        public void GetChapters_bulk_with_negative_id_throws_BadRequestException()
        {
            // T-13-05 mitigation: bulk endpoint must reject non-positive ids — mirrors TV
            // peer's seriesIds positive-int check at RenameEpisodeController.cs:41-44.
            Assert.Throws<BadRequestException>(() => Subject.GetChapters(new List<int> { 1, -5, 3 }));

            Mocker.GetMock<IRenameChapterFileService>()
                  .Verify(s => s.GetRenamePreviews(It.IsAny<List<int>>()), Times.Never);
        }
    }
}
