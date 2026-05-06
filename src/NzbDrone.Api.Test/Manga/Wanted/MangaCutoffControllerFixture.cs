using System;
using System.Collections.Generic;
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
    }
}
