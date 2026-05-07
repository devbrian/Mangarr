using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Sonarr.Api.V5.Manga.Chapter;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace NzbDrone.Api.Test.Manga.Chapter
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 13 Plan 13-07
    // (D-13-04 forward-prophylactic + D-13-07 Series-rename family rule).
    //
    // Role-match analog: src/NzbDrone.Api.Test/Manga/Wanted/MangaCutoffControllerFixture.cs
    // (Phase 12 Plan 12-12 — canonical IBroadcastSignalRMessage SetUp + Pattern 3 base-class
    // assertion + Pattern 5 IHandle broadcast tests for SignalR-broadcasting controllers) +
    // src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs (Plan 10-05 — predicate-locked
    // BroadcastMessage Verify shape).
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test
    // does not project-reference Sonarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs (Plan 10-05 Rule 3 deviation).
    //
    // Per-plan unit-test filter (Plan 13-07 verify): dotnet test
    //   --filter "FullyQualifiedName~ChapterFileController" must return >=1 passing test.
    //
    // This fixture provides:
    //   1. Reflective Attribute lookup confirms BARE [V5ApiController] (no Resource override)
    //      and that the runtime-derived SignalR resource name from
    //      ChapterFileResource.ResourceName is the lowercase literal "chapterfile" — the
    //      contract that the frontend SignalRListener.tsx Plan 13-07 Task 3 handler entry
    //      MUST match per Plan 13-00 Pattern κ.
    //   2. Base-class generic-type assertion confirms ChapterFileController extends
    //      RestControllerWithSignalR<ChapterFileResource, ChapterFile> (NOT plain Controller).
    //      Pin protects against silent regression to the plain-Controller shape.
    //   3. Happy-path GET-by-mangaId delegates to IChapterFileService.GetFilesByManga(mangaId).
    //   4. Happy-path GET-by-chapterFileIds delegates to IChapterFileService.Get(IEnumerable<int>).
    //   5. IHandle<ChapterFileAddedEvent> broadcasts ModelAction.Updated for the file's id.
    //   6. IHandle<ChapterFileDeletedEvent> broadcasts ModelAction.Deleted for the file's id.
    //
    // BroadcastResourceChange path (RestControllerWithSignalR.cs:52-67) requires
    // IBroadcastSignalRMessage.IsConnected to be true AND requires the controller's
    // GetResourceById(int) override to return a non-null resource for the Updated path —
    // SetUp wires both with a default ChapterFile so the broadcast reaches BroadcastMessage.
    [TestFixture]
    public class ChapterFileControllerFixture : TestBase<ChapterFileController>
    {
        [SetUp]
        public void Setup()
        {
            // RestControllerWithSignalR short-circuits BroadcastMessage when IsConnected
            // is false. Force true so the Mocker observes the BroadcastMessage call in
            // the IHandle<...> tests below.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(true);

            // BroadcastResourceChange(ModelAction.Updated, int) round-trips through
            // GetResourceById -> _chapterFileService.Get(id). Default mock returns null,
            // which would NRE when ToResource() runs against a null ChapterFile. Provide
            // a default non-null ChapterFile for any id so the broadcast reaches
            // BroadcastMessage. Per-test Setups can override with .Setup(s => s.Get(specificId))
            // for id-binding assertions.
            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.Get(It.IsAny<int>()))
                  .Returns<int>(id => new ChapterFile
                  {
                      Id = id,
                      MangaId = 42,
                      ChapterId = 1,
                      RelativePath = "test.cbz",
                      Path = "/manga/test/test.cbz"
                  });

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetManga(It.IsAny<int>()))
                  .Returns<int>(mangaId => new NzbDrone.Core.Manga.Manga
                  {
                      Id = mangaId,
                      Title = "Test Manga",
                      Path = "/manga/test"
                  });
        }

        // ===================== Pattern 2 — reflective Attribute lookup =====================

        [Test]
        public void Bare_V5ApiController_attribute_yields_default_resource_token()
        {
            // Bare [V5ApiController] (no explicit Resource arg) defaults Resource to the
            // [controller] token per VersionedApiControllerAttribute.cs:13 — the actual
            // SignalR resource name is computed at runtime from
            // new ChapterFileResource().ResourceName.Trim('/') per
            // RestControllerWithSignalR.cs:23-33. The combination guarantees the SignalR
            // resource name matches the lowercase literal "chapterfile" — the contract
            // that the frontend SignalRListener.tsx Plan 13-07 Task 3 handler entry
            // MUST match per Plan 13-00 Pattern κ.
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(ChapterFileController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull("ChapterFileController must carry [V5ApiController] for v5 routing");
            attr.Resource.Should().Be(VersionedApiControllerAttribute.CONTROLLER_RESOURCE,
                "bare [V5ApiController] should leave Resource at the [controller] token so " +
                "RestControllerWithSignalR auto-derives from ChapterFileResource.ResourceName");
        }

        [Test]
        public void ChapterFileResource_ResourceName_yields_lowercase_chapterfile_signalr_token()
        {
            // RestResource.ResourceName returns GetType().Name.ToLowerInvariant().Replace("resource", "")
            // per RestResource.cs:11 — the runtime-derived SignalR resource name is
            // therefore "chapterfile" (all lowercase, no camelCase). The frontend
            // SignalRListener.tsx Plan 13-07 Task 3 handler entry MUST match this
            // exact lowercase string per Plan 13-00 Pattern κ.
            new ChapterFileResource().ResourceName.Should().Be("chapterfile",
                "RestResource.ResourceName lowercases the type name and strips 'resource' — " +
                "the frontend SignalRListener.tsx handler entry must match this exact " +
                "lowercase token to consume the broadcast");
        }

        // ===================== Pattern 3 — base-class generic-type assertion =====================

        [Test]
        public void Controller_extends_RestControllerWithSignalR_per_Plan_13_07_contract()
        {
            // Pin the base-class shape so a future Phase 8/15 collapse cannot silently
            // revert to plain Controller (which would dead-letter the SignalR emission
            // contract). The assertion is on the open generic to avoid coupling to
            // TResource/TModel name changes — the LOAD-bearing fact is "this controller
            // has the SignalR base".
            typeof(ChapterFileController).BaseType.Should().NotBeNull();
            typeof(ChapterFileController).BaseType!.IsGenericType.Should().BeTrue(
                "ChapterFileController must extend a generic SignalR base — Plan 13-07 contract");
            typeof(ChapterFileController).BaseType!.GetGenericTypeDefinition()
                .Should().Be(typeof(RestControllerWithSignalR<,>),
                    "ChapterFileController must extend RestControllerWithSignalR<,> so the React " +
                    "Query cache for ['/chapterfile'] auto-refreshes on chapter file pipeline events");
        }

        // ===================== Happy-path delegation tests =====================

        [Test]
        public void GetChapterFiles_by_mangaId_calls_GetFilesByManga()
        {
            var files = new List<ChapterFile>
            {
                new() { Id = 1, MangaId = 42, ChapterId = 1, RelativePath = "ch01.cbz" },
                new() { Id = 2, MangaId = 42, ChapterId = 2, RelativePath = "ch02.cbz" },
            };

            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.GetFilesByManga(42))
                  .Returns(files);

            var result = Subject.GetChapterFiles(42, new List<int>());

            result.Result.Should().BeOfType<Ok<List<ChapterFileResource>>>();
            var ok = (Ok<List<ChapterFileResource>>)result.Result;
            ok.Value.Should().HaveCount(2);
            ok.Value.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2 });

            Mocker.GetMock<IChapterFileService>()
                  .Verify(s => s.GetFilesByManga(42), Times.Once);
            Mocker.GetMock<IChapterFileService>()
                  .Verify(s => s.Get(It.IsAny<IEnumerable<int>>()), Times.Never);
        }

        [Test]
        public void GetChapterFiles_by_chapterFileIds_calls_Get_bulk()
        {
            var ids = new List<int> { 5, 6, 7 };
            var files = ids.Select(i => new ChapterFile
            {
                Id = i,
                MangaId = 42,
                ChapterId = i,
                RelativePath = $"ch{i:D2}.cbz"
            }).ToList();

            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.Get(It.Is<IEnumerable<int>>(e => e.SequenceEqual(ids))))
                  .Returns(files);

            var result = Subject.GetChapterFiles(null, ids);

            result.Result.Should().BeOfType<Ok<List<ChapterFileResource>>>();
            ((Ok<List<ChapterFileResource>>)result.Result).Value.Should().HaveCount(3);

            Mocker.GetMock<IChapterFileService>()
                  .Verify(s => s.Get(It.Is<IEnumerable<int>>(e => e.SequenceEqual(ids))), Times.Once);
            Mocker.GetMock<IChapterFileService>()
                  .Verify(s => s.GetFilesByManga(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void GetChapterFiles_with_no_params_throws_BadRequestException()
        {
            // T-13-05 mitigation: neither mangaId nor chapterFileIds → 400 (mirrors
            // EpisodeFileController.GetEpisodeFiles precedent).
            Assert.Throws<BadRequestException>(() => Subject.GetChapterFiles(null, new List<int>()));
        }

        // ===================== Pattern 5 — IHandle broadcast tests =====================

        [Test]
        public void Handle_ChapterFileAddedEvent_should_BroadcastResourceChange_Updated_with_chapter_file_id()
        {
            // ChapterFileAddedEvent ctor: (ChapterFile) — verified
            // src/NzbDrone.Core/MediaFiles/Events/ChapterFileAddedEvent.cs:13.
            // Mirrors TV EpisodeFileController.Handle(EpisodeFileAddedEvent) at
            // src/Sonarr.Api.V5/EpisodeFiles/EpisodeFileController.cs:194-198.
            var chapterFile = Builder<ChapterFile>.CreateNew()
                .With(c => c.Id = 99)
                .With(c => c.MangaId = 42)
                .With(c => c.ChapterId = 7)
                .With(c => c.RelativePath = "added.cbz")
                .With(c => c.Path = "/manga/test/added.cbz")
                .Build();

            Subject.Handle(new ChapterFileAddedEvent(chapterFile));

            // Predicate locked per W6 (revision iteration 1) — verbatim shape extracted
            // from RestControllerWithSignalR.cs:81-89 (SignalRMessage construction confirms
            // Name = Resource = "chapterfile" + Action = ModelAction.Updated). The name
            // literal is the load-bearing contract between this controller and the
            // frontend SignalRListener.tsx Plan 13-07 Task 3 handler entry.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Updated &&
                      m.Name == "chapterfile" &&
                      ((ResourceChangeMessage<ChapterFileResource>)m.Body).Resource.Id == 99)),
                      Times.Once);
        }

        [Test]
        public void Handle_ChapterFileDeletedEvent_should_BroadcastResourceChange_Deleted_with_chapter_file_id()
        {
            // ChapterFileDeletedEvent ctor: (ChapterFile, DeleteMediaFileReason) — verified
            // src/NzbDrone.Core/MediaFiles/Events/ChapterFileDeletedEvent.cs:12.
            // Mirrors TV EpisodeFileController.Handle(EpisodeFileDeletedEvent) at
            // src/Sonarr.Api.V5/EpisodeFiles/EpisodeFileController.cs:200-204 — TV peer
            // also broadcasts ModelAction.Deleted by the deleted file's id (NOT by ChapterId).
            var chapterFile = Builder<ChapterFile>.CreateNew()
                .With(c => c.Id = 77)
                .With(c => c.MangaId = 42)
                .With(c => c.ChapterId = 7)
                .With(c => c.RelativePath = "deleted.cbz")
                .Build();

            Subject.Handle(new ChapterFileDeletedEvent(chapterFile, DeleteMediaFileReason.Manual));

            // Deleted action: BroadcastResourceChange(ModelAction.Deleted, id) constructs
            // a `new TResource { Id = id }` body per RestControllerWithSignalR.cs:60-62
            // (does NOT round-trip through GetResourceById). Verify the SignalRMessage
            // shape matches: Name = "chapterfile", Action = ModelAction.Deleted, Resource.Id = 77.
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Deleted &&
                      m.Name == "chapterfile" &&
                      ((ResourceChangeMessage<ChapterFileResource>)m.Body).Resource.Id == 77)),
                      Times.Once);
        }
    }
}
