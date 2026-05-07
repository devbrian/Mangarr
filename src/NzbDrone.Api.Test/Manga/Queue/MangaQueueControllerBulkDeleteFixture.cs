using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.Test.Common;
using Sonarr.Api.V5.Manga.Queue;
using Sonarr.Api.V5.Queue;

namespace NzbDrone.Api.Test.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller fixture per Phase 13 Plan 13-12
    // (gap closure for smoke-test quick-260507-p13 finding F-01 — `/api/v5/manga/queue/bulk`
    // DELETE returned HTTP 404). Pairs with the new [HttpDelete("bulk")] RemoveMany action on
    // src/Sonarr.Api.V5/Manga/Queue/MangaQueueController.cs (the CRUD controller, NOT
    // MangaQueueActionController — bulk DELETE belongs on the CRUD controller per TV peer
    // canonical pattern at QueueController.cs:97-136).
    //
    // Role-match analog: src/Sonarr.Api.V5/Queue/QueueController.cs:97-136 RemoveMany — the TV
    // peer this fixture's Subject mirrors (HttpDelete("bulk") action template + iterate
    // resource.Ids + return TypedResults.NoContent()). Manga's simplified Remove signature
    // (no blocklist/skipRedownload/changeCategory v1 params) per the existing [RestDeleteById]
    // precedent at MangaQueueController.cs:75-80 — Phase 15 collapse will unify the signature.
    //
    // Fixture lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because Sonarr.Core.Test
    // does not project-reference Sonarr.Api.V5; Sonarr.Api.Test does. Same convention as
    // src/NzbDrone.Api.Test/Manga/Queue/MangaQueueDetailsControllerFixture.cs (Plan 13-08) and
    // src/NzbDrone.Api.Test/Manga/Queue/MangaQueueActionControllerFixture.cs (Plan 13-10) —
    // documented in src/Sonarr.Api.V5/Manga/CLAUDE.md lines 108-111.
    //
    // Per-plan unit-test filter: `dotnet test --filter
    // "FullyQualifiedName~MangaQueueControllerBulkDeleteFixture"` must return >= 3 passing
    // tests. Pitfall 4 ordering rule: Verify(...) Times.Exactly(N) is asserted AFTER the
    // RemoveMany invocation (NOT inside SetUp).
    [TestFixture]
    public class MangaQueueControllerBulkDeleteFixture : TestBase<MangaQueueController>
    {
        [Test]
        public void Bulk_DELETE_with_empty_ids_returns_NoContent_and_calls_Remove_zero_times()
        {
            // Empty Ids list must short-circuit cleanly: no IMangaQueueService.Remove calls,
            // still returns HTTP 204 NoContent (mirrors TV QueueController.RemoveMany at
            // QueueController.cs:97-136 — empty Ids loop simply yields no work).
            var resource = new QueueBulkResource { Ids = new System.Collections.Generic.List<int>() };

            var result = Subject.RemoveMany(resource);

            result.Should().NotBeNull();
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(x => x.Remove(It.IsAny<int>()), Times.Exactly(0));
        }

        [Test]
        public void Bulk_DELETE_with_three_ids_returns_NoContent_and_calls_Remove_three_times()
        {
            // Happy-path: three distinct queue ids → IMangaQueueService.Remove called exactly
            // three times (once per id). Each call is asserted by-id so a future regression
            // that filters / dedupes / reorders the loop iteration is caught at this fixture.
            var resource = new QueueBulkResource
            {
                Ids = new System.Collections.Generic.List<int> { 11, 22, 33 },
            };

            var result = Subject.RemoveMany(resource);

            result.Should().NotBeNull();
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(x => x.Remove(It.IsAny<int>()), Times.Exactly(3));
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(x => x.Remove(11), Times.Once);
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(x => x.Remove(22), Times.Once);
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(x => x.Remove(33), Times.Once);
        }

        [Test]
        public void Action_template_is_HttpDelete_bulk()
        {
            // Pitfall 6 coexistence — MangaQueueController shares [V5ApiController("manga/queue")]
            // with MangaQueueActionController. ASP.NET Core MVC discriminates by action template,
            // so the new RemoveMany action template MUST be exactly "bulk" (NOT "bulk/{id}" or
            // unconstrained). Smoke-test F-01 (quick-260507-p13) caught a 404 here before this
            // fixture pinned the template — protect future regressions.
            var removeMany = typeof(MangaQueueController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault(m => m.Name == "RemoveMany"
                                      && m.GetParameters().Length == 1
                                      && m.GetParameters()[0].ParameterType == typeof(QueueBulkResource));

            removeMany.Should().NotBeNull("MangaQueueController must expose RemoveMany(QueueBulkResource resource)");

            var del = removeMany!.GetCustomAttribute<HttpDeleteAttribute>();
            del.Should().NotBeNull("RemoveMany(QueueBulkResource) must be annotated with [HttpDelete]");
            del!.Template.Should().Be("bulk",
                "RemoveMany action template must be exactly \"bulk\" so DELETE /api/v5/manga/queue/bulk routes correctly " +
                "(F-01 gap closure from smoke-test quick-260507-p13)");
        }

        // ===================== CR-02 (Phase 13 Plan 13-13) — RemoveMany hardening =====================

        [Test]
        public void Bulk_DELETE_attribute_includes_Consumes_application_json()
        {
            // CR-02 hardening: pin [Consumes("application/json")] on RemoveMany so OpenAPI v5
            // doc generation surfaces the request media-type correctly. Without [Consumes], the
            // generated spec may emit `*/*` accept-list (breaking typed-client codegen) and
            // form-urlencoded posts may bind `resource` as null (NRE on resource.Ids enumeration).
            // Sibling endpoints (MangaQueueActionController.Grab, ChapterFileController bulk
            // DELETE) standardize on this attribute.
            var removeMany = typeof(MangaQueueController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault(m => m.Name == "RemoveMany"
                                      && m.GetParameters().Length == 1
                                      && m.GetParameters()[0].ParameterType == typeof(QueueBulkResource));

            removeMany.Should().NotBeNull();

            var consumes = removeMany!.GetCustomAttribute<ConsumesAttribute>();
            consumes.Should().NotBeNull("RemoveMany(QueueBulkResource) must be annotated with [Consumes(\"application/json\")] " +
                                        "(CR-02 sibling-endpoint consistency + OpenAPI v5 codegen contract)");
            consumes!.ContentTypes.Should().Contain("application/json",
                "RemoveMany [Consumes] attribute must list 'application/json' as the bound request media type");
        }

        [Test]
        public void Bulk_DELETE_with_null_resource_returns_NoContent_without_calling_Remove()
        {
            // CR-02 null-guard: a missing or null body must short-circuit to NoContent rather
            // than NRE inside the foreach. ASP.NET Core can deliver `resource = null` when the
            // body is absent / unparseable — we MUST handle that without exploding.
            var result = Subject.RemoveMany(null!);

            result.Should().NotBeNull();
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(x => x.Remove(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void Bulk_DELETE_with_null_ids_returns_NoContent_without_calling_Remove()
        {
            // CR-02 null-guard part 2: a body with `Ids = null` (e.g., `{}`) must short-circuit
            // to NoContent rather than NRE on the .Ids.Distinct() / foreach iteration.
            var resource = new QueueBulkResource { Ids = null! };

            var result = Subject.RemoveMany(resource);

            result.Should().NotBeNull();
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(x => x.Remove(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void Bulk_DELETE_with_duplicate_ids_calls_Remove_once_per_unique_id()
        {
            // CR-02 dedupe: duplicate ids in the request body must be collapsed via .Distinct()
            // BEFORE iterating. Each IMangaQueueService.Remove call publishes a
            // MangaQueueUpdatedEvent which triggers a SignalR Sync broadcast — duplicates would
            // otherwise N-times multiply UI thrash. Mirrors TV QueueController.cs:122/127
            // DistinctBy semantics.
            //
            // Payload: 5 ids, 2 distinct (11, 22) — Remove must be called exactly twice.
            var resource = new QueueBulkResource
            {
                Ids = new System.Collections.Generic.List<int> { 11, 22, 11, 22, 11 },
            };

            var result = Subject.RemoveMany(resource);

            result.Should().NotBeNull();
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(
                      x => x.Remove(It.IsAny<int>()),
                      Times.Exactly(2),
                      "duplicate ids must be deduped via .Distinct() before invoking Remove " +
                      "(prevents redundant SignalR Sync broadcasts on the manga queue)");
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(x => x.Remove(11), Times.Once);
            Mocker.GetMock<IMangaQueueService>()
                  .Verify(x => x.Remove(22), Times.Once);
        }
    }
}
