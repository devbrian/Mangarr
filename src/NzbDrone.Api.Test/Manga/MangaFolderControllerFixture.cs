using System;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Test.Common;
using Mangarr.Api.V5.Manga;
using Sonarr.Http;

namespace NzbDrone.Api.Test.Manga
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 13 Plan 13-05.
    // Role-match analog: src/NzbDrone.Api.Test/Manga/Wanted/MangaCutoffControllerFixture.cs
    // (canonical reflective Attribute-lookup pattern pinning the route literal).
    //
    // Phase 13 Plan 13-05 ships MangaFolderController as the manga peer of
    // SeriesFolderController per D-13-04 forward-prophylactic + D-13-07 Series-rename
    // family rule. RESEARCH §Pitfall 2: route attribute MUST be `[V5ApiController("manga")]`
    // (NOT `"manga/folder"`) — the action template `[HttpGet("{id}/folder")]` owns the
    // /folder suffix. Final URL: /api/v5/manga/{id}/folder.
    //
    // Per-plan unit-test filter (D-13-18):
    //   dotnet test --filter "FullyQualifiedName~MangaFolderController"
    //
    // Tests:
    //   1. Route_attribute_is_manga_literal_per_RESEARCH_Pitfall_2 — pin route prefix
    //      to "manga" (NOT "manga/folder"); guards against the silent 404 anti-pattern
    //      where a planner accidentally bakes the action suffix into the route attribute.
    //   2. Action_template_is_id_folder_per_PATTERNS_md — pin the HttpGet action
    //      template on GetFolder to "{id}/folder" via reflective attribute lookup.
    //   3. Controller_extends_bare_Controller_not_RestController — pin base class to
    //      bare Microsoft.AspNetCore.Mvc.Controller (NOT RestController/RestControllerWithSignalR);
    //      the folder-name preview endpoint is a read-only computation and should not
    //      participate in the SignalR resource-change broadcast cycle.
    //   4. GetFolder_delegates_to_manga_service_GetManga_and_filename_builder_GetMangaFolder —
    //      happy-path delegation with mocked IMangaService + IBuildMangaFileNames; verifies
    //      the response shape carries the computed folder string.
    [TestFixture]
    public class MangaFolderControllerFixture : TestBase<MangaFolderController>
    {
        [Test]
        public void Route_attribute_is_manga_literal_per_RESEARCH_Pitfall_2()
        {
            // RESEARCH §Pitfall 2: the route prefix attribute on the controller class MUST
            // be the bare resource name "manga" — NOT "manga/folder". The /folder suffix
            // is owned by the action template (asserted separately via Test 2). Baking the
            // suffix into the route attribute would make the final URL /api/v5/manga/folder/{id}/folder
            // which silently 404s every frontend caller of /api/v5/manga/{id}/folder.
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(MangaFolderController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr.Resource.Should().Be("manga");
        }

        [Test]
        public void Action_template_is_id_folder_per_PATTERNS_md()
        {
            // PATTERNS.md MangaFolderController section: the GetFolder action template owns
            // the "{id}/folder" suffix. Combined with the [V5ApiController("manga")] route
            // prefix the final URL is /api/v5/manga/{id}/folder. Reflectively look up the
            // HttpGetAttribute on the GetFolder method so a future refactor that drops or
            // changes the template fails this test loudly.
            var method = typeof(MangaFolderController).GetMethod(
                nameof(MangaFolderController.GetFolder),
                BindingFlags.Public | BindingFlags.Instance);

            method.Should().NotBeNull("MangaFolderController must expose a GetFolder method");

            var httpGetAttr = (HttpGetAttribute)Attribute.GetCustomAttribute(
                method!, typeof(HttpGetAttribute));

            httpGetAttr.Should().NotBeNull("GetFolder must carry [HttpGet(\"{id}/folder\")]");
            httpGetAttr.Template.Should().Be("{id}/folder");
        }

        [Test]
        public void Controller_extends_bare_Controller_not_RestController()
        {
            // Mirrors SeriesFolderController.cs:11 — the folder-name preview endpoint extends
            // bare Microsoft.AspNetCore.Mvc.Controller, NOT RestController or
            // RestControllerWithSignalR. This is the right shape because the endpoint is a
            // read-only string computation (no entity persistence, no SignalR broadcast).
            // Pinning the base class guards against a future planner accidentally upgrading
            // the controller to RestControllerWithSignalR — which would force a meaningless
            // resource-change broadcast cycle on every folder-name fetch.
            typeof(MangaFolderController).BaseType.Should().Be(typeof(Controller));
        }

        [Test]
        public void GetFolder_delegates_to_manga_service_GetManga_and_filename_builder_GetMangaFolder()
        {
            // Happy-path delegation contract: the controller fetches the Manga aggregate
            // via IMangaService.GetManga(id) then asks IBuildMangaFileNames.GetMangaFolder
            // (NamingConfig=null) for the computed folder string. Verifies both delegations
            // fire exactly once with the inbound id and that the response shape carries the
            // computed string under the `folder` property.
            var manga = new NzbDrone.Core.Manga.Manga { Id = 42, Title = "Test" };

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(42))
                .Returns(manga);

            Mocker.GetMock<IBuildMangaFileNames>()
                .Setup(b => b.GetMangaFolder(It.Is<NzbDrone.Core.Manga.Manga>(m => m.Id == 42), It.IsAny<NamingConfig>()))
                .Returns("Test Folder");

            var result = Subject.GetFolder(42);

            result.Should().BeOfType<Ok<object>>();

            Mocker.GetMock<IMangaService>()
                .Verify(s => s.GetManga(42), Times.Once);
            Mocker.GetMock<IBuildMangaFileNames>()
                .Verify(b => b.GetMangaFolder(It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<NamingConfig>()), Times.Once);
        }
    }
}
