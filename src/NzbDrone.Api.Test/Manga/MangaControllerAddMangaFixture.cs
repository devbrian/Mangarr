using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Mangarr.Api.V5.Manga;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Manga
{
    // Bug fix new-manga-default-monitored (2026-05-08) — pins the
    // MangaController.AddManga default-Monitored=true contract.
    //
    // Background: the frontend Add Manga form's `AddMangaPayload` does not
    // historically carry the manga-level `monitored: bool` (only the per-Chapter
    // `monitor: MangaMonitor` enum). After JSON deserialization at the backend,
    // `MangaResource.Monitored` defaulted to the C# bool default = false, the
    // mapper carried that into `Manga.Monitored = false`, AddMangaService
    // PrepareForAdd's gap-05 block (AddMangaService.cs:298-301) only flips
    // FALSE on Monitor=None — so manga always landed unmonitored.
    //
    // The fix: the AddManga controller action defaults `Monitored = true` on
    // the inbound model unless the payload explicitly carried `AddOptions.Monitor
    // == MangaMonitor.None`. This complements (does not replace) the existing
    // gap-05 block which still forces FALSE on Monitor=None.
    //
    // Per-fixture unit-test filter:
    //   dotnet test --filter "FullyQualifiedName~MangaControllerAddMangaFixture"
    //
    // Tests:
    //   1. AddManga_with_no_AddOptions_defaults_Monitored_true — covers the legacy
    //      callers that send the Phase 2 minimal payload (no AddOptions block).
    //   2. AddManga_with_AddOptions_Monitor_All_defaults_Monitored_true — covers
    //      the canonical user-clicks-Add-with-Monitor-All path.
    //   3. AddManga_with_AddOptions_Monitor_non_None_defaults_Monitored_true —
    //      covers the other 3 non-None monitor values via parameterized helper.
    //   4. AddManga_with_AddOptions_Monitor_None_preserves_Monitored_false —
    //      explicit-opt-out: Monitor=None must NOT be overridden to true.
    //   5. AddManga_propagates_AddOptions_into_persisted_model — guards the
    //      MangaResourceMapper.ToModel AddOptions copy that closes the second
    //      leg of the bug (frontend Monitor was previously dropped on the wire).
    [TestFixture]
    public class MangaControllerAddMangaFixture : TestBase<MangaController>
    {
        [SetUp]
        public void Setup()
        {
            // IAddMangaService stub: capture the inbound Manga and echo it back as
            // the "added" manga so the controller's TypedResults.Created path
            // round-trips a non-null resource.
            Mocker.GetMock<IAddMangaService>()
                  .Setup(s => s.AddManga(It.IsAny<NzbDrone.Core.Manga.Manga>()))
                  .Returns<NzbDrone.Core.Manga.Manga>(m =>
                  {
                      m.Id = 17;
                      return m;
                  });

            // MapResource → ComputeStatistics calls IChapterService.GetChaptersByManga;
            // empty list keeps the path NRE-free.
            Mocker.GetMock<IChapterService>()
                  .Setup(s => s.GetChaptersByManga(It.IsAny<int>()))
                  .Returns(new List<NzbDrone.Core.Manga.Chapter>());

            // RestControllerWithSignalR.BroadcastResourceChange short-circuits when
            // IsConnected is false (the AddManga action does not directly broadcast,
            // but downstream IHandle paths reach the broadcaster — keep it inert in
            // these tests).
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(false);

            // The AddManga action calls Url.Action(...) to construct the 201 Created
            // location header. Without a ControllerContext + IUrlHelper the call
            // throws ArgumentNullException("helper"). Set a stub that returns a
            // non-null path so the TypedResults.Created builder succeeds.
            var urlHelper = new Mock<IUrlHelper>();
            urlHelper.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("/api/v5/manga/17");
            Subject.Url = urlHelper.Object;
        }

        private static MangaResource BuildResource(AddMangaOptionsResource addOptions)
        {
            // Mirror the wire shape: monitored defaults to false (the bug —
            // MangaResource.Monitored was the C# bool default). The PostValidator
            // requires at least one cross-source ID to be set (PostValidator.RuleFor
            // in MangaController ctor); we provide MangaDexId for that contract.
            var resource = Builder<MangaResource>.CreateNew()
                .With(r => r.Title = "Test Manga")
                .With(r => r.MangaDexId = System.Guid.NewGuid())
                .With(r => r.RootFolderPath = "C:\\Manga")
                .With(r => r.Monitored = false) // simulates the wire-default after deserialization
                .With(r => r.AddOptions = addOptions)
                .Build();

            return resource;
        }

        [Test]
        public void AddManga_with_no_AddOptions_defaults_Monitored_true()
        {
            // Scenario: legacy Phase 2 minimal-payload caller — no AddOptions block.
            // Bug pre-fix would persist Monitored=false; the controller default
            // must coerce it to true.
            var resource = BuildResource(addOptions: null);

            var result = Subject.AddManga(resource);

            result.Should().NotBeNull();

            Mocker.GetMock<IAddMangaService>()
                  .Verify(s => s.AddManga(It.Is<NzbDrone.Core.Manga.Manga>(m => m.Monitored)),
                          Times.Once,
                          "AddManga must default Monitored=true when no AddOptions present");
        }

        [Test]
        public void AddManga_with_AddOptions_Monitor_All_defaults_Monitored_true()
        {
            // Canonical UI path — user clicks Add with Monitor=All (the form default).
            var resource = BuildResource(new AddMangaOptionsResource { Monitor = MangaMonitor.All });

            Subject.AddManga(resource);

            Mocker.GetMock<IAddMangaService>()
                  .Verify(s => s.AddManga(It.Is<NzbDrone.Core.Manga.Manga>(m => m.Monitored)),
                          Times.Once,
                          "AddManga must default Monitored=true when Monitor=All");
        }

        [TestCase(MangaMonitor.All)]
        [TestCase(MangaMonitor.Future)]
        [TestCase(MangaMonitor.Missing)]
        [TestCase(MangaMonitor.Latest)]
        public void AddManga_with_AddOptions_Monitor_non_None_defaults_Monitored_true(MangaMonitor monitor)
        {
            // Parameterized over the 4 non-None MangaMonitor values; all must coerce
            // Manga.Monitored to true at the controller layer.
            var resource = BuildResource(new AddMangaOptionsResource { Monitor = monitor });

            Subject.AddManga(resource);

            Mocker.GetMock<IAddMangaService>()
                  .Verify(s => s.AddManga(It.Is<NzbDrone.Core.Manga.Manga>(m => m.Monitored)),
                          Times.Once,
                          $"AddManga must default Monitored=true when Monitor={monitor}");
        }

        [Test]
        public void AddManga_with_AddOptions_Monitor_None_preserves_Monitored_false()
        {
            // Explicit opt-out — user chose Monitor=None on the form. The controller
            // default must NOT flip this to true; AddMangaService.PrepareForAdd's
            // gap-05 block also forces false in this branch (defense in depth).
            var resource = BuildResource(new AddMangaOptionsResource { Monitor = MangaMonitor.None });

            Subject.AddManga(resource);

            Mocker.GetMock<IAddMangaService>()
                  .Verify(s => s.AddManga(It.Is<NzbDrone.Core.Manga.Manga>(m => !m.Monitored)),
                          Times.Once,
                          "AddManga must preserve Monitored=false when user picked Monitor=None");
        }

        [Test]
        public void AddManga_with_AddOptions_omitting_Monitor_defaults_All_and_Monitored_true()
        {
            // #357 ordinal-reorder regression guard (Codex PR #358 review): after `None`
            // became the zero ordinal, a partial `addOptions` payload that OMITS `monitor`
            // deserializes `AddMangaOptionsResource.Monitor` to null. ToModel must map null ->
            // MangaMonitor.All (the prior default-0 behavior), NOT None — otherwise the manga
            // would silently land unmonitored. Distinct from the explicit-None opt-out above.
            var resource = BuildResource(new AddMangaOptionsResource
            {
                Monitor = null, // simulates `"addOptions": { "searchForMissingChapters": true }`
                SearchForMissingChapters = true,
            });

            Subject.AddManga(resource);

            Mocker.GetMock<IAddMangaService>()
                  .Verify(s => s.AddManga(It.Is<NzbDrone.Core.Manga.Manga>(m =>
                              m.Monitored &&
                              m.AddOptions != null &&
                              m.AddOptions.Monitor == MangaMonitor.All)),
                          Times.Once,
                          "omitted Monitor must default to All (Monitored=true), not the new zero-ordinal None");
        }

        [Test]
        public void AddManga_propagates_AddOptions_into_persisted_model()
        {
            // Second leg of the new-manga-default-monitored bug: the
            // MangaResourceMapper.ToModel previously dropped the AddOptions field
            // entirely (it didn't exist on MangaResource). The fix added the
            // mapping — this test guards against a regression that re-drops it.
            //
            // Without AddOptions on the persisted Manga, MangaScannedHandler bails
            // out early (MangaScannedHandler.cs:65-71 checks `addOptions == null`)
            // and SetChapterMonitoredStatus never runs — leaving every chapter
            // unmonitored regardless of the user's MonitorAll choice.
            var resource = BuildResource(new AddMangaOptionsResource
            {
                Monitor = MangaMonitor.All,
                SearchForMissingChapters = true,
            });

            Subject.AddManga(resource);

            Mocker.GetMock<IAddMangaService>()
                  .Verify(s => s.AddManga(It.Is<NzbDrone.Core.Manga.Manga>(m =>
                              m.AddOptions != null &&
                              m.AddOptions.Monitor == MangaMonitor.All &&
                              m.AddOptions.SearchForMissingChapters)),
                          Times.Once,
                          "AddManga must propagate AddOptions (Monitor + SearchForMissingChapters) into the persisted model");
        }
    }
}
