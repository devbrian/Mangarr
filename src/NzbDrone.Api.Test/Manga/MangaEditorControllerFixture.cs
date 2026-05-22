using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Mangarr.Api.V5.Manga;
using Mangarr.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Core.RootFolders;
using NzbDrone.Test.Common;
namespace NzbDrone.Api.Test.Manga
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 13 Plan 13-04 (sub-wave C
    // F-CUTOFF-class silent-404 closure for /manga/editor PUT + DELETE — useManga.ts:608+:655).
    //
    // Role-match analog: src/NzbDrone.Api.Test/Manga/Wanted/MangaCutoffControllerFixture.cs
    // (Plan 12-12 reflective Attribute lookup pattern pinning route literal to "manga/editor")
    // + src/NzbDrone.Api.Test/Manga/Chapter/ChapterControllerFixture.cs (Plan 07-01 D-07 — the
    // canonical AutoMoqer + TestBase<TController> pattern for V5 controller-shape tests).
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test
    // does not project-reference Mangarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs (Plan 10-05 Rule 3 deviation).
    //
    // Per-plan unit-test filter (D-13-18 + Pattern S4):
    //   dotnet test --filter "FullyQualifiedName~MangaEditorController"
    // must return >= 1 passing test. This fixture provides 4 tests:
    //   1. Route literal pin — V5ApiController("manga/editor") attribute reflective lookup.
    //   2. Base-class pin — extends bare Controller (NOT RestController/RestControllerWithSignalR
    //      per RESEARCH §Pitfall 1 — bulk-action controller, no SignalR fan-out from this site;
    //      MangaController.IHandle<MangaUpdatedEvent> already broadcasts post-update).
    //   3. SaveAll PUT happy-path delegation — verifies foreach apply-deltas + UpdateManga call.
    //   4. DeleteManga DELETE 2-arg call — verifies RESEARCH §Pitfall 4 signature (drops the
    //      addImportListExclusion arg present on the TV peer; manga IMangaService.DeleteManga
    //      is 2-arg only — Import Lists deferred to v1.1 per PROJECT.md).
    [TestFixture]
    public class MangaEditorControllerFixture : TestBase<MangaEditorController>
    {
        [Test]
        public void Route_attribute_is_manga_editor_literal_per_plan_07_02_contract()
        {
            // Plan 07-02 URL-shaped React Query key contract — the route attribute string is the
            // load-bearing contract between the frontend useSaveMangaEditor + useBulkDeleteManga
            // hooks (useManga.ts:608+:655 PUT + DELETE) and this controller. Mismatched route
            // literal silently 404s every bulk-edit / bulk-delete from the manga library page
            // (the F-CUTOFF-class symptom Phase 13 Plan 13-04 closes).
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(MangaEditorController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr.Resource.Should().Be("manga/editor");
        }

        [Test]
        public void Controller_extends_bare_Controller_not_RestController_per_RESEARCH_Pitfall_1()
        {
            // RESEARCH §Pitfall 1: MangaEditorController MUST extend bare Controller — NOT
            // RestController<T> (CRUD-resource shape) or RestControllerWithSignalR<,> (SignalR
            // fan-out shape). Bulk-action controllers do not own a single resource id and must
            // not broadcast SignalR from the editor site (MangaController.IHandle<MangaUpdatedEvent>
            // / IHandle<MangaBulkEditedEvent> already broadcast post-update — broadcasting from
            // the editor would double-fire). Pin the base-class shape so a future Phase 15
            // collapse cannot silently swap to RestControllerWithSignalR<,>.
            typeof(MangaEditorController).BaseType.Should().Be(typeof(Controller),
                "MangaEditorController must extend bare Controller per RESEARCH §Pitfall 1; " +
                "swapping to RestController<T> or RestControllerWithSignalR<,> would break the " +
                "bulk-action contract and double-fire SignalR broadcasts");
        }

        [Test]
        public void SaveAll_PUT_delegates_to_manga_service_GetManga_and_UpdateManga()
        {
            // Happy-path delegation test: PUT /api/v5/manga/editor with field-deltas applies
            // them to each manga returned by IMangaService.GetManga(IEnumerable<int>) and then
            // calls IMangaService.UpdateManga(List<Manga>, useExistingRelativeFolder:!MoveFiles).
            var mangaList = new List<NzbDrone.Core.Manga.Manga>
            {
                new() { Id = 1, Title = "Manga A", Monitored = false, Tags = new HashSet<int>() },
                new() { Id = 2, Title = "Manga B", Monitored = false, Tags = new HashSet<int>() },
            };

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(It.IsAny<IEnumerable<int>>()))
                .Returns(mangaList);

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.UpdateManga(It.IsAny<List<NzbDrone.Core.Manga.Manga>>(), It.IsAny<bool>()))
                .Returns<List<NzbDrone.Core.Manga.Manga>, bool>((m, _) => m);

            // Plan 13-13 CR-03: MangaEditorValidator now wires TranslationProfileExistsValidator
            // (PropertyValidator that calls ITranslationProfileService.Exists). Mock the lookup
            // so the validator passes for the test's chosen TranslationProfileId == 7.
            Mocker.GetMock<ITranslationProfileService>()
                .Setup(s => s.Exists(7))
                .Returns(true);

            var resource = new MangaEditorResource
            {
                MangaIds = new List<int> { 1, 2 },
                Monitored = true,
                TranslationProfileId = 7,
                CustomFormatProfileId = 11,
            };

            var result = Subject.SaveAll(resource);

            // Assert: GetManga + UpdateManga both called once; field deltas applied to each row.
            Mocker.GetMock<IMangaService>()
                .Verify(s => s.GetManga(It.Is<IEnumerable<int>>(e => e.SequenceEqual(new[] { 1, 2 }))),
                    Times.Once);

            Mocker.GetMock<IMangaService>()
                .Verify(
                    s => s.UpdateManga(
                        It.Is<List<NzbDrone.Core.Manga.Manga>>(list =>
                            list.Count == 2 &&
                            list.All(m => m.Monitored == true) &&
                            list.All(m => m.TranslationProfileId == 7) &&
                            list.All(m => m.CustomFormatProfileId == 11)),
                        true),
                    Times.Once);

            // Result is the Ok branch carrying the mapped MangaResource list.
            result.Result.Should().BeOfType<Ok<List<MangaResource>>>();
        }

        [Test]
        public void DeleteManga_DELETE_threads_addImportListExclusion_true_through_to_3arg_service()
        {
            // GH #241 follow-up: the v1.1 ImportList substrate is live, so the Phase 13
            // RESEARCH §Pitfall 4 "deferred to v1.1" 2-arg shape no longer applies.
            // MangaEditorController.DeleteManga must thread resource.AddImportListExclusion
            // through to IMangaService.DeleteManga's 3-arg overload — otherwise the
            // FE Delete modal checkbox is silently ignored and the auto-exclusion always
            // fires (the 2-arg overload unconditionally defaults addImportListExclusion to
            // true). This test pins the true-path of the fix.
            var resource = new MangaEditorResource
            {
                MangaIds = new List<int> { 5, 9 },
                DeleteFiles = true,
                AddImportListExclusion = true,
            };

            var result = Subject.DeleteManga(resource);

            Mocker.GetMock<IMangaService>()
                .Verify(
                    s => s.DeleteManga(
                        It.Is<List<int>>(ids => ids.SequenceEqual(new[] { 5, 9 })),
                        true,
                        true),
                    Times.Once);

            result.Should().BeOfType<NoContent>();
        }

        [Test]
        public void DeleteManga_DELETE_threads_addImportListExclusion_false_through_to_3arg_service()
        {
            // GH #241 follow-up: pin the false-path. The bug report was specifically that
            // unchecking the FE "Add to Import List Exclusion" checkbox had no effect —
            // the controller must pass false through to the service so
            // ImportListExclusionService.Handle(MangaDeletedEvent) sees
            // message.AddImportListExclusion == false and skips the auto-exclusion insert.
            var resource = new MangaEditorResource
            {
                MangaIds = new List<int> { 5, 9 },
                DeleteFiles = false,
                AddImportListExclusion = false,
            };

            var result = Subject.DeleteManga(resource);

            Mocker.GetMock<IMangaService>()
                .Verify(
                    s => s.DeleteManga(
                        It.Is<List<int>>(ids => ids.SequenceEqual(new[] { 5, 9 })),
                        false,
                        false),
                    Times.Once);

            result.Should().BeOfType<NoContent>();
        }

        // ===================== CR-03 (Phase 13 Plan 13-13) — MangaEditorValidator existence checks =====================

        [Test]
        public void Validator_rejects_unknown_root_folder_path()
        {
            // CR-03 closure of T-13-03 mass-assignment mitigation: MangaEditorValidator now
            // wires RootFolderExistsValidator so a PUT with a rootFolderPath that does NOT
            // match any registered root folder is REJECTED with a ValidationException at the
            // controller boundary. Without the validator, IMangaService.UpdateManga would
            // silently mass-assign the bogus path across every row in mangaIds (defeating
            // T-13-03 — the "M" in mass-assignment).
            //
            // Test setup: mock IRootFolderService.All() to return a single root at "/manga"
            // and submit a request with rootFolderPath = "C:\\Windows\\System32" — the
            // validator must throw FluentValidation.ValidationException; UpdateManga must NOT
            // be invoked.
            var mangaList = new List<NzbDrone.Core.Manga.Manga>
            {
                new() { Id = 1, Title = "Manga A", Monitored = false, Tags = new HashSet<int>() },
            };

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(It.IsAny<IEnumerable<int>>()))
                .Returns(mangaList);

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.All())
                .Returns(new List<RootFolder>
                {
                    new() { Id = 1, Path = "/manga" },
                });

            var resource = new MangaEditorResource
            {
                MangaIds = new List<int> { 1 },
                RootFolderPath = "/not/a/registered/root",
            };

            // Controller throws FluentValidation.ValidationException on validator failure
            // (MangaEditorController.cs:114-117). The exception escapes the controller and
            // is mapped to HTTP 400 by the upstream pipeline; here we assert at the unit-test
            // layer that the throw fires before any UpdateManga delegation.
            Assert.Throws<ValidationException>(() => Subject.SaveAll(resource));

            // Hard-pin: UpdateManga must NOT be invoked when RootFolder validation fails.
            Mocker.GetMock<IMangaService>()
                .Verify(s => s.UpdateManga(It.IsAny<List<NzbDrone.Core.Manga.Manga>>(), It.IsAny<bool>()),
                    Times.Never,
                    "RootFolderExistsValidator failure must short-circuit BEFORE the UpdateManga " +
                    "persistence call — otherwise T-13-03 mass-assignment closure is incomplete");
        }
    }
}
