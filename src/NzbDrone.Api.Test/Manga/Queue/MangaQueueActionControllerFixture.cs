using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Mangarr.Api.V5.Manga.Queue;
using Mangarr.Http;
using Mangarr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.Test.Common;
namespace NzbDrone.Api.Test.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 13 Plan 13-10 (sub-wave A FINDINGS
    // §1.12 verdict = gap_in_scope; §2 cross-section reconciliation §1+§2 agreed; CONFIRMED in
    // 13-API-V5-SURFACE-FINDINGS.md line 988). Pairs with src/Mangarr.Api.V5/Manga/Queue/
    // MangaQueueActionController.cs (the bare-Controller bulk-action peer that backs useQueue.ts:178
    // grab + :205 grab-bulk for manga callers).
    //
    // Role-match analog: src/Mangarr.Api.V5/Queue/QueueActionController.cs (the TV peer this fixture's
    // Subject mirrors — bare Controller, bulk-action one-shot pattern, NOT RestControllerWithSignalR).
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test does
    // not project-reference Mangarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // src/NzbDrone.Api.Test/Manga/Wanted/MangaCutoffControllerFixture.cs (Plan 12-12 + Plan 10-05
    // Rule 3 deviation — documented in src/Mangarr.Api.V5/Manga/CLAUDE.md lines 108-111).
    //
    // Per-plan unit-test filter (Plan 13-10 PATTERNS.md S4): dotnet test --filter
    // "FullyQualifiedName~MangaQueueActionController" must return >= 1 passing test. This fixture
    // provides 5 tests covering the contract surface:
    //   1. Route_attribute_is_manga_queue_literal — reflective lookup of [V5ApiController("manga/queue")]
    //      pins the route prefix so the frontend useQueue.ts:178/:205 fetch path resolves.
    //   2. Action_template_is_grab_id_int_per_PATTERNS — reflective HttpPostAttribute lookup on the
    //      Grab(int) method asserts the action template "grab/{id:int}" (NOT "grab/{id}") so the
    //      route discriminator coexists with MangaQueueController per Pitfall 6.
    //   3. Controller_extends_bare_Controller — base-class assertion confirms NO RestControllerWithSignalR
    //      base (this is a one-shot bulk-action endpoint per RESEARCH §Pitfall 1; SignalR push lives
    //      on MangaQueueController.Handle(MangaQueueUpdatedEvent) instead).
    //   4. Grab_single_calls_FindPendingQueueItem_and_DownloadService — happy-path mock chain verifies
    //      IMangaDownloadService.DownloadReport is called once with the RemoteChapter directly
    //      (Phase 15 Wave (A) W-4 rebind 2026-05-07 — pre-Wave-(A) the test verified the
    //      RemoteChapter.ToRemoteEpisodeShim() conversion against IDownloadService).
    //   5. Grab_single_with_unknown_id_throws_NotFoundException — null-return short-circuit asserts
    //      404 propagation (mirrors TV QueueActionController.Grab line 30-32 pattern).
    [TestFixture]
    public class MangaQueueActionControllerFixture : TestBase<MangaQueueActionController>
    {
        [Test]
        public void Route_attribute_is_manga_queue_literal()
        {
            // Plan 07-02 URL-shaped React Query key contract — the route attribute string is the
            // load-bearing contract between the frontend useQueue.ts hook and this controller.
            // Pitfall 5 — TV/manga route MUST NOT collide; pin to "manga/queue" prefix.
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(MangaQueueActionController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr.Resource.Should().Be("manga/queue");
        }

        [Test]
        public void Action_template_is_grab_id_int_per_PATTERNS()
        {
            // Pitfall 6 coexistence — MangaQueueController already registers
            // [V5ApiController("manga/queue")] with [HttpGet] / [RestDeleteById]. ASP.NET Core MVC
            // discriminates by action template, so the new POST grab/{id:int} action template MUST
            // be exact (NOT "grab/{id}" — the :int constraint pins the route to integer ids only).
            //
            // Find the single-id Grab method (parameter is int, NOT QueueBulkResource).
            var grabSingle = typeof(MangaQueueActionController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault(m => m.Name == "Grab"
                                      && m.GetParameters().Length == 1
                                      && m.GetParameters()[0].ParameterType == typeof(int));

            grabSingle.Should().NotBeNull("MangaQueueActionController must expose Grab(int id)");

            var post = grabSingle!.GetCustomAttribute<HttpPostAttribute>();
            post.Should().NotBeNull("Grab(int id) must be annotated with [HttpPost]");
            post!.Template.Should().Be("grab/{id:int}");
        }

        [Test]
        public void Controller_extends_bare_Controller()
        {
            // RESEARCH §Pitfall 1 — bulk-action endpoints extend bare Controller (NOT
            // RestControllerWithSignalR<,>). Mirrors TV QueueActionController.cs line 12.
            // Phase 15 collapse symmetry depends on this base-class shape staying stable.
            typeof(MangaQueueActionController).BaseType.Should().Be(typeof(Controller),
                "MangaQueueActionController must extend bare Controller (not RestControllerWithSignalR) — " +
                "this is a one-shot bulk-action endpoint, NOT a SignalR-broadcasting CRUD surface");
        }

        [Test]
        public async Task Grab_single_calls_FindPendingQueueItem_and_DownloadService()
        {
            // Happy-path mock chain: FindPendingQueueItem(42) returns a MangaQueueItem with a
            // populated RemoteChapter; verify IMangaDownloadService.DownloadReport is called
            // exactly once with the RemoteChapter directly and a null downloadClientId
            // (Phase 15 Wave (A) W-4 rebind 2026-05-07 — pre-Wave-(A) the controller wrapped
            // the RemoteChapter through ToRemoteEpisodeShim and called IDownloadService).
            var release = new ReleaseInfo { Title = "Test Manga - Chapter 1", IndexerId = 1 };
            var manga = new NzbDrone.Core.Manga.Manga { Id = 99, Title = "Test Manga" };
            var chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 7, MangaId = 99, ChapterNumber = 1m },
            };
            var remoteChapter = new RemoteChapter
            {
                Release = release,
                Manga = manga,
                Chapters = chapters,
            };
            var queueItem = new MangaQueueItem
            {
                Id = 42,
                MangaId = 99,
                ChapterId = 7,
                Manga = manga,
                Chapters = chapters,
                RemoteChapter = remoteChapter,
            };

            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Setup(s => s.FindPendingQueueItem(42))
                  .Returns(queueItem);

            await Subject.Grab(42);

            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Verify(s => s.FindPendingQueueItem(42), Times.Once);

            Mocker.GetMock<IMangaDownloadService>()
                  .Verify(s => s.DownloadReport(
                      It.Is<RemoteChapter>(rc =>
                          rc.Release == release &&
                          rc.Manga != null &&
                          rc.Manga.Id == 99 &&
                          rc.Chapters != null &&
                          rc.Chapters.Count == 1 &&
                          rc.Chapters[0].Id == 7),
                      null),
                      Times.Once);
        }

        [Test]
        public void Grab_single_with_unknown_id_throws_NotFoundException()
        {
            // Mirror TV QueueActionController.Grab lines 28-32: null pending-release → NotFoundException
            // (framework maps to HTTP 404).
            Mocker.GetMock<IMangaPendingReleaseService>()
                  .Setup(s => s.FindPendingQueueItem(999))
                  .Returns((MangaQueueItem)null!);

            Assert.ThrowsAsync<NotFoundException>(async () => await Subject.Grab(999));

            // No download-service call should have happened.
            Mocker.GetMock<IMangaDownloadService>()
                  .Verify(s => s.DownloadReport(It.IsAny<RemoteChapter>(), It.IsAny<int?>()),
                          Times.Never);
        }
    }
}
