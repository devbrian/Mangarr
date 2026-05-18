using Mangarr.Api.V5.Tags;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Tags;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Tags
{
    // Phase 24 Plan 24-04 — AT-08 re-wire coverage for TagController. Two-axis
    // surface:
    //   1. New IHandle<AutoTagsUpdatedEvent> path broadcasts ModelAction.Sync.
    //   2. Existing IHandle<TagsUpdatedEvent> path still works (no regression
    //      from the sibling-add).
    //
    // Project placement note (Rule 3 deviation): plan-frontmatter listed the
    // fixture at src/NzbDrone.Core.Test/Tags/ but Mangarr.Core.Test does NOT
    // project-reference Mangarr.Api.V5 (verified at src/NzbDrone.Core.Test/Mangarr.Core.Test.csproj).
    // Phase 10 Plan 10-05 hit the same constraint and placed Mangarr.Api.V5
    // fixtures under NzbDrone.Api.Test/ — the project that already references
    // Mangarr.Api.V5. Mirrors MangaControllerSignalRFixture.cs structure.
    //
    // RestControllerWithSignalR.BroadcastResourceChange short-circuits if
    // IBroadcastSignalRMessage.IsConnected is false (RestControllerWithSignalR.cs:52);
    // SetUp forces it true so the mock observes BroadcastMessage. The handler
    // calls the parameterless BroadcastResourceChange(ModelAction.Sync) overload
    // (RestControllerWithSignalR.cs:74-82) which builds a SignalRMessage with
    // ResourceName = "tag" (derived from TagResource.cs RestResource.ResourceName
    // override).
    [TestFixture]
    public class TagControllerAutoTagsUpdatedFixture : TestBase<TagController>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .SetupGet(b => b.IsConnected)
                  .Returns(true);
        }

        [Test]
        public void Handle_AutoTagsUpdatedEvent_should_BroadcastResourceChange_Sync()
        {
            Subject.Handle(new AutoTagsUpdatedEvent());

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Sync && m.Name == "tag")),
                      Times.Once);
        }

        [Test]
        public void Handle_TagsUpdatedEvent_should_still_broadcast_Sync_after_AutoTags_sibling_add()
        {
            // Sibling regression check: the Phase 15 strip removed the
            // IHandle<AutoTagsUpdatedEvent> declaration; this plan adds it back
            // alongside the EXISTING IHandle<TagsUpdatedEvent>. Verify the
            // existing handler still fires.
            Subject.Handle(new TagsUpdatedEvent());

            Mocker.GetMock<IBroadcastSignalRMessage>()
                  .Verify(b => b.BroadcastMessage(It.Is<SignalRMessage>(m =>
                      m.Action == ModelAction.Sync && m.Name == "tag")),
                      Times.Once);
        }
    }
}
