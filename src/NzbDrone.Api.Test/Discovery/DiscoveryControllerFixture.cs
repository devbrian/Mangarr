using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Mangarr.Api.V5.Discovery;
using Mangarr.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Discovery;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Discovery
{
    // Phase 42 Plan 42-04 (DISC-02 / DISC-03 / DISC-07) — controller-shape fixture for the
    // Discovery REST surface. Lives under NzbDrone.Api.Test (NOT NzbDrone.Core.Test) because
    // Mangarr.Core.Test does not project-reference Mangarr.Api.V5 — same convention as
    // MangaEditorControllerFixture / MangaControllerSignalRFixture.
    //
    // Pins: the admin-auth attribute (V5ApiController("discovery")), the bare-Controller base
    // (Pitfall 7 — no SignalR double-fan-out), the search validate→delegate→map path, and the
    // bulk-add enqueue (202 + the T-42-04-DOS clamp to 100 ids).
    [TestFixture]
    public class DiscoveryControllerFixture : TestBase<DiscoveryController>
    {
        [Test]
        public void Route_attribute_is_discovery_literal()
        {
            // The route literal is the load-bearing contract between the FE Discovery hooks and
            // this controller. A mismatch silently 404s every browse / bulk-add call.
            var attr = (V5ApiControllerAttribute)Attribute.GetCustomAttribute(
                typeof(DiscoveryController), typeof(V5ApiControllerAttribute));

            attr.Should().NotBeNull();
            attr.Resource.Should().Be("discovery");
        }

        [Test]
        public void Controller_extends_bare_Controller_not_SignalR_base_per_Pitfall_7()
        {
            // 42-PATTERNS §Pitfall 7: DiscoveryController MUST extend bare Controller — Discovery
            // has no live entity; the add fan-out rides MangaController.IHandle<MangaImportedEvent>.
            // Swapping to RestControllerWithSignalR<,> would double-fire SignalR on every add.
            typeof(DiscoveryController).BaseType.Should().Be(typeof(Controller),
                "DiscoveryController must extend bare Controller per Pitfall 7 — no SignalR fan-out from this site");
        }

        [Test]
        public void Search_validates_then_delegates_to_DiscoveryService_and_maps_response()
        {
            var serviceResult = new DiscoveryResult
            {
                Results = new List<DiscoveryResultItem>
                {
                    new() { MangaBakaId = 42, Title = "Solo Leveling", Score = 9.1m },
                },
                PoolExhausted = true,
                Requested = 10,
                Found = 1,
            };

            Mocker.GetMock<IDiscoveryService>()
                .Setup(s => s.Search(It.IsAny<DiscoveryFilter>(), It.IsAny<int>()))
                .Returns(serviceResult);

            var resource = new DiscoverySearchRequestResource
            {
                Type = new List<string> { "manhwa" },
                Status = new List<string> { "releasing" },
                SortBy = "score_desc",
                X = 10,
            };

            var result = Subject.Search(resource);

            // Search delegates the X + the mapped filter to the eligibility loop.
            Mocker.GetMock<IDiscoveryService>()
                .Verify(s => s.Search(It.IsAny<DiscoveryFilter>(), 10), Times.Once);

            // Ok branch carrying the mapped envelope.
            result.Result.Should().BeOfType<Ok<DiscoverySearchResponseResource>>();
            var ok = (Ok<DiscoverySearchResponseResource>)result.Result;
            ok.Value!.Results.Should().HaveCount(1);
            ok.Value.Results[0].MangaBakaId.Should().Be(42);
            ok.Value.PoolExhausted.Should().BeTrue();
            ok.Value.Requested.Should().Be(10);
            ok.Value.Found.Should().Be(1);
        }

        [Test]
        public void Search_returns_BadRequest_and_does_not_call_service_when_X_out_of_range()
        {
            // T-42-04-DOS / T-42-04-TAMPER: an out-of-range X (151 > 100 ceiling) must be
            // rejected at the validator BEFORE the filter reaches DiscoveryService.Search.
            var resource = new DiscoverySearchRequestResource { X = 151 };

            var result = Subject.Search(resource);

            result.Result.Should().BeOfType<BadRequest>();

            Mocker.GetMock<IDiscoveryService>()
                .Verify(s => s.Search(It.IsAny<DiscoveryFilter>(), It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void Search_returns_BadRequest_when_enum_filter_value_is_not_whitelisted()
        {
            // T-42-04-TAMPER: an unknown type token must not reach MangaBakaApi.Browse.
            var resource = new DiscoverySearchRequestResource
            {
                Type = new List<string> { "not-a-real-type" },
                X = 10,
            };

            var result = Subject.Search(resource);

            result.Result.Should().BeOfType<BadRequest>();

            Mocker.GetMock<IDiscoveryService>()
                .Verify(s => s.Search(It.IsAny<DiscoveryFilter>(), It.IsAny<int>()), Times.Never);
        }

        // A fully-populated, validator-passing bulk-add payload (non-empty ids, root folder,
        // positive profile ids) — the happy-path baseline the validation tests mutate.
        private static DiscoveryBulkAddResource ValidBulkAddResource(IEnumerable<int> ids) =>
            new()
            {
                MangaBakaIds = ids.ToList(),
                RootFolderPath = "/manga",
                TranslationProfileId = 1,
                CustomFormatProfileId = 1,
                SearchForMissingChapters = true,
            };

        [Test]
        public void BulkAdd_enqueues_command_once_and_returns_202_Accepted()
        {
            var resource = ValidBulkAddResource(new[] { 1, 2, 3 });

            var result = Subject.BulkAdd(resource);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.Is<DiscoveryBulkAddCommand>(c =>
                            c.MangaBakaIds.SequenceEqual(new[] { 1, 2, 3 }) &&
                            c.RootFolderPath == "/manga" &&
                            c.SearchForMissingChapters),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);

            // 202 fire-and-forget (D-07).
            result.Result.Should().BeOfType<Accepted>();
        }

        [Test]
        public void BulkAdd_clamps_more_than_100_ids_to_100_before_enqueue()
        {
            // T-42-04-DOS: a 150-id bulk-add must be trimmed to 100 (the grid's max X) before
            // the refresh storm is enqueued.
            var resource = ValidBulkAddResource(Enumerable.Range(1, 150));

            DiscoveryBulkAddCommand captured = null;
            Mocker.GetMock<IManageCommandQueue>()
                .Setup(q => q.Push(It.IsAny<DiscoveryBulkAddCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                .Callback<DiscoveryBulkAddCommand, CommandPriority, CommandTrigger>((c, _, _) => captured = c)
                .Returns(new CommandModel());

            Subject.BulkAdd(resource);

            captured.Should().NotBeNull();
            captured.MangaBakaIds.Should().HaveCount(100);
            captured.MangaBakaIds.Should().Equal(Enumerable.Range(1, 100));
        }

        [Test]
        public void BulkAdd_returns_BadRequest_and_does_not_enqueue_when_ids_empty()
        {
            // PR #396 review #3: an empty id list is an invalid command — reject at the
            // boundary (400) instead of accepting a no-op with 202.
            var resource = ValidBulkAddResource(Enumerable.Empty<int>());

            var result = Subject.BulkAdd(resource);

            result.Result.Should().BeOfType<BadRequest>();

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(It.IsAny<DiscoveryBulkAddCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                    Times.Never);
        }

        [Test]
        public void BulkAdd_returns_BadRequest_and_does_not_enqueue_when_root_folder_missing()
        {
            // PR #396 review #3: a missing root folder would fail later in background
            // execution — reject the payload shape up front.
            var resource = ValidBulkAddResource(new[] { 1 });
            resource.RootFolderPath = null;

            var result = Subject.BulkAdd(resource);

            result.Result.Should().BeOfType<BadRequest>();

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(It.IsAny<DiscoveryBulkAddCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                    Times.Never);
        }

        [Test]
        public void BulkAdd_returns_BadRequest_when_profile_ids_not_positive()
        {
            // PR #396 review #3: profile ids resolve by id downstream; 0/negative is never valid.
            var resource = ValidBulkAddResource(new[] { 1 });
            resource.TranslationProfileId = 0;

            var result = Subject.BulkAdd(resource);

            result.Result.Should().BeOfType<BadRequest>();

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(It.IsAny<DiscoveryBulkAddCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                    Times.Never);
        }
    }
}
