using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Test.Common;
using Sonarr.Api.V5.Manga.Wanted;
using Sonarr.Http;

namespace NzbDrone.Api.Test.Manga.Wanted
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 12 Plan 12-12 (sub-wave-B-addition #4 —
    // F-CUTOFF closure).
    // Role-match analog: src/NzbDrone.Api.Test/Manga/Chapter/ChapterControllerFixture.cs (Plan 07-01 D-07 —
    // canonical AutoMoqer + TestBase<TController> pattern for V5 controller-shape tests).
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test does not
    // project-reference Sonarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs (Plan 10-05 Rule 3 deviation —
    // documented in src/Sonarr.Api.V5/Manga/CLAUDE.md lines 108-111).
    //
    // Per-plan unit-test filter (D-12-20): dotnet test --filter "FullyQualifiedName~MangaCutoff" must
    // return at least 1 passing test. This fixture provides 2 tests:
    //   1. Happy-path paged GET delegates to IChapterCutoffService.ChaptersWhereCutoffUnmet exactly once.
    //   2. Reflective Attribute lookup confirms route literal "manga/wanted/cutoff" — the contract that
    //      the frontend Plan 12-08 fetch path expects per Plan 07-02 URL-shaped key contract.
    //
    // MangaCutoffController extends plain Controller (NOT EpisodeControllerWithSignalR), so this
    // fixture does NOT need the Mocker.SetConstant<IBroadcastSignalRMessage> SetUp call from
    // ChapterControllerFixture:35-43.
    [TestFixture]
    public class MangaCutoffControllerFixture : TestBase<MangaCutoffController>
    {
        [Test]
        public void GetCutoffUnmetChapters_delegates_to_chapter_cutoff_service()
        {
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
                new() { Id = 2, MangaId = 42, ChapterNumber = 2m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            var result = Subject.GetCutoffUnmetChapters(new PagingRequestResource());

            result.Should().BeOfType<Ok<PagingResource<MangaCutoffResource>>>();
            var ok = (Ok<PagingResource<MangaCutoffResource>>)result;
            ok.Value!.Records.Should().HaveCount(2);

            Mocker.GetMock<IChapterCutoffService>()
                .Verify(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()),
                    Times.Once);
        }

        [Test]
        public void Route_attribute_is_manga_wanted_cutoff_literal_per_plan_07_02_contract()
        {
            // Plan 07-02 URL-shaped React Query key contract — the route attribute string is the
            // load-bearing contract between the frontend useCutoffUnmet hook (Plan 12-08) and this
            // controller. Mismatched route literal silently breaks every fetch from
            // /manga/wanted/cutoffunmet (the F-CUTOFF symptom Plan 12-99 Task 7 surfaced before
            // Plan 12-12 shipped). Pitfall 5 — TV/manga cache MUST NOT collide.
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(MangaCutoffController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr.Resource.Should().Be("manga/wanted/cutoff");
        }

        // Phase 12 REVIEW MED-01 — defense-in-depth tests asserting the four observable
        // controller behaviors the route-attribute + happy-path tests do not exercise:
        //   * `monitored=true` adds a `c.Monitored == true` FilterExpression
        //   * `mangaIds[]` (non-empty) adds a `mangaIds.Contains(c.MangaId)` FilterExpression
        //   * `includeManga=true` triggers IMangaService.GetManga(MangaId) hydration in MapToResource
        //   * `includeManga=true` populates MangaCutoffResource.Manga with {Id, Title}
        // These pin the FilterExpressions chain + subresource hydration so a future Phase 15
        // collapse / schema change cannot silently strip filter wiring while the smoke test
        // stays green (Phase 11 CR-02 lesson — mocks must verify against the real contract path,
        // not just structural existence).

        [Test]
        public void GetCutoffUnmetChapters_applies_monitored_filter_when_monitored_true()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            Subject.GetCutoffUnmetChapters(new PagingRequestResource(), monitored: true);

            capturedSpec.Should().NotBeNull();
            capturedSpec!.FilterExpressions.Should().NotBeEmpty();

            // Compile + invoke the captured filter expressions against synthetic Chapter rows
            // to verify the monitored filter actually selects only Monitored == true rows.
            var monitoredChapter = new NzbDrone.Core.Manga.Chapter { Monitored = true };
            var unmonitoredChapter = new NzbDrone.Core.Manga.Chapter { Monitored = false };

            var allFiltersPass = capturedSpec.FilterExpressions
                .Select(e => e.Compile())
                .ToList();

            allFiltersPass.All(f => f(monitoredChapter)).Should().BeTrue(
                "monitored=true filter should accept Monitored chapter rows");
            allFiltersPass.Any(f => !f(unmonitoredChapter)).Should().BeTrue(
                "monitored=true filter should reject Unmonitored chapter rows");
        }

        [Test]
        public void GetCutoffUnmetChapters_omits_monitored_filter_when_monitored_false()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            // monitored=false short-circuits the `if (monitored)` branch — no Monitored
            // FilterExpression is added, so unmonitored chapters surface in the result.
            Subject.GetCutoffUnmetChapters(new PagingRequestResource(), monitored: false);

            capturedSpec.Should().NotBeNull();

            // With no other filters supplied, FilterExpressions must be empty.
            capturedSpec!.FilterExpressions.Should().BeEmpty(
                "monitored=false should not add any FilterExpression");
        }

        [Test]
        public void GetCutoffUnmetChapters_applies_mangaIds_filter_when_mangaIds_non_empty()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            // monitored=false to isolate the mangaIds filter.
            Subject.GetCutoffUnmetChapters(new PagingRequestResource(), monitored: false, mangaIds: new[] { 42, 99 });

            capturedSpec.Should().NotBeNull();
            capturedSpec!.FilterExpressions.Should().HaveCount(1,
                "mangaIds filter should add exactly one FilterExpression when monitored=false");

            // Compile + invoke the captured mangaIds filter against synthetic Chapter rows.
            var matchingChapter = new NzbDrone.Core.Manga.Chapter { MangaId = 42 };
            var nonMatchingChapter = new NzbDrone.Core.Manga.Chapter { MangaId = 7 };

            var compiledFilter = capturedSpec.FilterExpressions[0].Compile();
            compiledFilter(matchingChapter).Should().BeTrue(
                "mangaIds filter should accept rows whose MangaId is in the supplied list");
            compiledFilter(nonMatchingChapter).Should().BeFalse(
                "mangaIds filter should reject rows whose MangaId is not in the supplied list");
        }

        [Test]
        public void GetCutoffUnmetChapters_skips_mangaIds_filter_when_mangaIds_empty_or_null()
        {
            PagingSpec<NzbDrone.Core.Manga.Chapter> capturedSpec = null;

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    capturedSpec = spec;
                    spec.Records = new List<NzbDrone.Core.Manga.Chapter>();
                    spec.TotalRecords = 0;
                    return spec;
                });

            Subject.GetCutoffUnmetChapters(new PagingRequestResource(), monitored: false, mangaIds: Array.Empty<int>());

            capturedSpec.Should().NotBeNull();
            capturedSpec!.FilterExpressions.Should().BeEmpty(
                "empty mangaIds array should not add a FilterExpression");
        }

        [Test]
        public void GetCutoffUnmetChapters_hydrates_Manga_subresource_when_includeManga_true()
        {
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(42))
                .Returns(new NzbDrone.Core.Manga.Manga { Id = 42, Title = "Test Manga" });

            var result = Subject.GetCutoffUnmetChapters(new PagingRequestResource(), includeManga: true);

            // IMangaService.GetManga must be called exactly once for the single chapter row.
            Mocker.GetMock<IMangaService>()
                .Verify(s => s.GetManga(42), Times.Once);

            result.Should().BeOfType<Ok<PagingResource<MangaCutoffResource>>>();
            var ok = (Ok<PagingResource<MangaCutoffResource>>)result;

            // The Manga subresource must be populated on each row with {Id, Title}.
            var record = ok.Value!.Records.Single();
            record.Manga.Should().NotBeNull();
            record.Manga!.Id.Should().Be(42);
            record.Manga.Title.Should().Be("Test Manga");
        }

        [Test]
        public void GetCutoffUnmetChapters_skips_Manga_subresource_hydration_when_includeManga_false()
        {
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 1, MangaId = 42, ChapterNumber = 1m, Monitored = true, ChapterType = ChapterType.Regular },
            };

            Mocker.GetMock<IChapterCutoffService>()
                .Setup(s => s.ChaptersWhereCutoffUnmet(It.IsAny<PagingSpec<NzbDrone.Core.Manga.Chapter>>()))
                .Returns<PagingSpec<NzbDrone.Core.Manga.Chapter>>(spec =>
                {
                    spec.Records = chapters;
                    spec.TotalRecords = chapters.Count;
                    return spec;
                });

            // Default includeManga = false (no explicit arg).
            var result = Subject.GetCutoffUnmetChapters(new PagingRequestResource());

            Mocker.GetMock<IMangaService>()
                .Verify(
                    s => s.GetManga(It.IsAny<int>()),
                    Times.Never,
                    "includeManga=false must NOT trigger IMangaService.GetManga hydration");

            result.Should().BeOfType<Ok<PagingResource<MangaCutoffResource>>>();
            var ok = (Ok<PagingResource<MangaCutoffResource>>)result;
            ok.Value!.Records.Single().Manga.Should().BeNull(
                "Manga subresource must remain null when includeManga=false");
        }
    }
}
