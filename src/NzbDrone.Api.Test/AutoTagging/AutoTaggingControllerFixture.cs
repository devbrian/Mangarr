using System.Collections.Generic;
using FluentAssertions;
using Mangarr.Api.V5.AutoTagging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.AutoTagging
{
    // Phase 24 Plan 24-04 — V5 AutoTaggingController fixture covering the CRUD
    // round-trip (the plumbing surface) + the 3 event-fire test methods that pin
    // the controller -> service -> event flow (W-4 gate per plan threat register
    // T-24-04-07).
    //
    // Project placement note (Rule 3 deviation): plan-frontmatter listed the
    // fixture at src/Mangarr.Api.V5.Test/AutoTagging/ but no such project exists
    // (verified by ls src/ + ls src/Mangarr.Api.V5.Test). Phase 23 hit the same
    // dilemma and placed Mangarr.Api.V5 fixtures under NzbDrone.Api.Test/ (the
    // existing project that already project-references Mangarr.Api.V5 — see
    // src/NzbDrone.Api.Test/Mangarr.Api.Test.csproj). Mirroring the Phase 10
    // Plan 10-05 precedent at src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs.
    //
    // Service-layer event-fire was already proven in Plan 24-02 by
    // AutoTaggingServiceFixture (the service publishes AutoTagsUpdatedEvent on
    // Insert/Update/Delete). What this fixture verifies is that the controller
    // does NOT short-circuit the service — every CRUD endpoint delegates straight
    // to the service exactly once, and we wire the mock service so that its
    // Insert/Update/Delete also invoke a mock IEventAggregator's
    // PublishEvent<AutoTagsUpdatedEvent>. The Times.Once assertion on
    // BOTH the service-method invocation AND the event-publish invocation is the
    // W-4 plumbing-not-swallowed assertion.
    [TestFixture]
    public class AutoTaggingControllerFixture : TestBase<AutoTaggingController>
    {
        private List<IAutoTaggingSpecification> _specifications = null!;

        [SetUp]
        public void Setup()
        {
            // Inject an empty spec catalog — the controller's CRUD endpoints don't
            // need spec-resolution behavior for the event-fire flow; ToModel will
            // only fail spec-lookup if the test sends an AutoTaggingResource with
            // a non-empty Specifications[] list. CRUD tests send empty specs.
            _specifications = new List<IAutoTaggingSpecification>();

            Mocker.SetConstant<IEnumerable<IAutoTaggingSpecification>>(_specifications);

            // Wire the mock IAutoTaggingService so its Insert/Update/Delete each
            // invoke the mock IEventAggregator's PublishEvent<AutoTagsUpdatedEvent>
            // exactly once — mirroring the AutoTaggingService Sonarr-canonical
            // behavior verified by AutoTaggingServiceFixture (Plan 24-02). This
            // is the controller -> service -> event glue under test.
            var eventAggregator = Mocker.GetMock<IEventAggregator>();

            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.Insert(It.IsAny<AutoTag>()))
                  .Returns<AutoTag>(at =>
                  {
                      at.Id = 7;
                      eventAggregator.Object.PublishEvent(new AutoTagsUpdatedEvent());
                      return at;
                  });

            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.Update(It.IsAny<AutoTag>()))
                  .Callback<AutoTag>(_ => eventAggregator.Object.PublishEvent(new AutoTagsUpdatedEvent()));

            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.Delete(It.IsAny<int>()))
                  .Callback<int>(_ => eventAggregator.Object.PublishEvent(new AutoTagsUpdatedEvent()));

            // GetById is invoked by TypedCreated/TypedAccepted to serialize the
            // response body — return a fresh AutoTag with the same id so the
            // mapper round-trips cleanly. Hashset/list defaults avoid NREs in
            // ToResource.
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetById(It.IsAny<int>()))
                  .Returns<int>(id => new AutoTag
                  {
                      Id = id,
                      Name = "test",
                      Tags = new HashSet<int> { 1 },
                      Specifications = new List<IAutoTaggingSpecification>()
                  });

            // RestController.TypedCreated/TypedAccepted call Url.Action(...) which
            // requires an IUrlHelper. Without setting Subject.Url the call throws
            // ArgumentNullException("helper"). Mirror the MangaControllerAddMangaFixture
            // precedent (Phase 10) — stub the helper to return a non-null path.
            var urlHelper = new Mock<IUrlHelper>();
            urlHelper.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("/api/v5/autotagging/7");
            Subject.Url = urlHelper.Object;
        }

        // ===================== CRUD round-trip surface =====================

        [Test]
        public void GetAll_returns_resources()
        {
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.All())
                  .Returns(new List<AutoTag>
                  {
                      new AutoTag { Id = 1, Name = "rule-a", Tags = new HashSet<int> { 1 } },
                      new AutoTag { Id = 2, Name = "rule-b", Tags = new HashSet<int> { 2 } }
                  });

            var ok = Subject.GetAll();
            ok.Should().NotBeNull();
            ok.Value.Should().HaveCount(2);
            ok.Value![0].Name.Should().Be("rule-a");
        }

        [Test]
        public void GetAll_returns_empty_list_on_fresh_db()
        {
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.All())
                  .Returns(new List<AutoTag>());

            var ok = Subject.GetAll();
            ok.Should().NotBeNull();
            ok.Value.Should().BeEmpty();
        }

        // ===================== W-4 event-fire gate =====================

        [Test]
        public void Insert_publishes_event()
        {
            // Controller -> service -> event flow: Create invokes
            // _autoTaggingService.Insert exactly once; the mock service in SetUp
            // bridges to PublishEvent<AutoTagsUpdatedEvent> exactly once.
            var resource = new AutoTaggingResource
            {
                Name = "test",
                Tags = new HashSet<int> { 1 },
                Specifications = new List<AutoTaggingSpecificationResource>()
            };

            Subject.Create(resource);

            Mocker.GetMock<IAutoTaggingService>()
                  .Verify(s => s.Insert(It.IsAny<AutoTag>()), Times.Once);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<AutoTagsUpdatedEvent>()), Times.Once);
        }

        [Test]
        public void Update_publishes_event()
        {
            var resource = new AutoTaggingResource
            {
                Id = 7,
                Name = "renamed",
                Tags = new HashSet<int> { 1 },
                Specifications = new List<AutoTaggingSpecificationResource>()
            };

            Subject.Update(resource);

            Mocker.GetMock<IAutoTaggingService>()
                  .Verify(s => s.Update(It.IsAny<AutoTag>()), Times.Once);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<AutoTagsUpdatedEvent>()), Times.Once);
        }

        [Test]
        public void Delete_publishes_event()
        {
            Subject.DeleteAutoTagging(7);

            Mocker.GetMock<IAutoTaggingService>()
                  .Verify(s => s.Delete(7), Times.Once);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<AutoTagsUpdatedEvent>()), Times.Once);
        }
    }
}
