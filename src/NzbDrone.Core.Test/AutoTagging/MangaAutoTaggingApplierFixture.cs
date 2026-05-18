using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging
{
    // Phase 24 v1.1 Wave 3 — MangaAutoTaggingApplier behavior coverage.
    //
    // Verifies:
    //   * 3 IHandle paths (AutoTagsUpdatedEvent / MangaAddedEvent /
    //     MangaRefreshCompleteEvent) all route through ApplyChanges.
    //   * publishUpdatedEvent: false is the only UpdateManga overload used.
    //   * Short-circuit: empty changes skip the UpdateManga call entirely.
    //   * AT-07 user-applied-tags-sticky: user's tag (no rule references it) is
    //     preserved when GetTagChanges returns TagsToRemove containing only the
    //     rule's own tag.
    [TestFixture]
    public class MangaAutoTaggingApplierFixture : CoreTest<MangaAutoTaggingApplier>
    {
        private NzbDrone.Core.Manga.Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = new NzbDrone.Core.Manga.Manga
            {
                Id = 1,
                Title = "Test Manga",
                Tags = new HashSet<int>()
            };

            // Default GetTagChanges -> empty (short-circuit)
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(It.IsAny<NzbDrone.Core.Manga.Manga>()))
                  .Returns(new AutoTaggingChanges());

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(new List<NzbDrone.Core.Manga.Manga> { _manga });
        }

        // ===================== MangaAddedEvent =====================

        [Test]
        public void Handle_MangaAddedEvent_applies_tags_when_rule_matches()
        {
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(_manga))
                  .Returns(new AutoTaggingChanges { TagsToAdd = new HashSet<int> { 99 } });

            Subject.Handle(new MangaAddedEvent(_manga));

            _manga.Tags.Should().Contain(99);
            Mocker.GetMock<IMangaService>()
                  .Verify(s => s.UpdateManga(_manga, false), Times.Once);
        }

        [Test]
        public void Handle_MangaAddedEvent_with_empty_changes_short_circuits_without_UpdateManga()
        {
            // Default GetTagChanges = empty (Setup). Short-circuit must skip UpdateManga.
            Subject.Handle(new MangaAddedEvent(_manga));

            Mocker.GetMock<IMangaService>()
                  .Verify(s => s.UpdateManga(It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<bool>()),
                          Times.Never);
        }

        // ===================== AutoTagsUpdatedEvent =====================

        [Test]
        public void Handle_AutoTagsUpdatedEvent_iterates_full_library_and_applies_only_to_matching_manga()
        {
            var manga1 = new NzbDrone.Core.Manga.Manga { Id = 1, Title = "Match A", Tags = new HashSet<int>() };
            var manga2 = new NzbDrone.Core.Manga.Manga { Id = 2, Title = "Match B", Tags = new HashSet<int>() };
            var manga3 = new NzbDrone.Core.Manga.Manga { Id = 3, Title = "No Match", Tags = new HashSet<int>() };

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(new List<NzbDrone.Core.Manga.Manga> { manga1, manga2, manga3 });

            // Two of three mangas get a tag-add; the third returns empty changes.
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(manga1))
                  .Returns(new AutoTaggingChanges { TagsToAdd = new HashSet<int> { 99 } });
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(manga2))
                  .Returns(new AutoTaggingChanges { TagsToAdd = new HashSet<int> { 99 } });
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(manga3))
                  .Returns(new AutoTaggingChanges());

            Subject.Handle(new AutoTagsUpdatedEvent());

            Mocker.GetMock<IMangaService>()
                  .Verify(s => s.UpdateManga(It.IsAny<NzbDrone.Core.Manga.Manga>(), false),
                          Times.Exactly(2),
                          "Only the 2 matching mangas should produce UpdateManga calls");
        }

        // ===================== MangaRefreshCompleteEvent =====================

        [Test]
        public void Handle_MangaRefreshCompleteEvent_re_evaluates_full_library()
        {
            var manga1 = new NzbDrone.Core.Manga.Manga { Id = 1, Tags = new HashSet<int>() };
            var manga2 = new NzbDrone.Core.Manga.Manga { Id = 2, Tags = new HashSet<int>() };

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(new List<NzbDrone.Core.Manga.Manga> { manga1, manga2 });

            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(It.IsAny<NzbDrone.Core.Manga.Manga>()))
                  .Returns(new AutoTaggingChanges { TagsToAdd = new HashSet<int> { 5 } });

            Subject.Handle(new MangaRefreshCompleteEvent());

            Mocker.GetMock<IMangaService>()
                  .Verify(s => s.UpdateManga(It.IsAny<NzbDrone.Core.Manga.Manga>(), false),
                          Times.Exactly(2));
        }

        // ===================== AT-07 user-applied-tags-sticky =====================

        [Test]
        public void User_tags_sticky_when_RemoveTagsAutomatically_false_and_rule_does_not_match()
        {
            // Scenario: Manga has user-applied tag {1} + rule-applied tag {99}.
            // Rule has RemoveTagsAutomatically=false, no longer matches.
            // GetTagChanges returns empty TagsToRemove (per AutoTaggingService.cs
            // L126-132 -- only iterates removal under RemoveTagsAutomatically=true).
            // Result: both tags untouched (user's tag 1 stays; rule's tag 99 stays).
            _manga.Tags.Add(1);
            _manga.Tags.Add(99);

            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(_manga))
                  .Returns(new AutoTaggingChanges());

            Subject.Handle(new MangaAddedEvent(_manga));

            _manga.Tags.Should().BeEquivalentTo(new[] { 1, 99 },
                "RemoveTagsAutomatically=false -- D-05 default -- user-applied tags stick");
        }

        [Test]
        public void User_tags_sticky_when_RemoveTagsAutomatically_true_only_rule_tags_removed()
        {
            // Scenario: Manga has user-applied tag {1} + rule-applied tag {99}.
            // Rule has RemoveTagsAutomatically=true, no longer matches.
            // GetTagChanges returns TagsToRemove={99} (rule's own tag only -- user's
            // tag 1 is NOT in any rule's Tags set so by construction never appears
            // in TagsToRemove).
            // Result: rule's tag 99 removed; user's tag 1 preserved.
            _manga.Tags.Add(1);
            _manga.Tags.Add(99);

            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(_manga))
                  .Returns(new AutoTaggingChanges { TagsToRemove = new HashSet<int> { 99 } });

            Subject.Handle(new MangaAddedEvent(_manga));

            _manga.Tags.Should().BeEquivalentTo(new[] { 1 },
                "AT-07: rule's own tag removed; user-applied tag stays sticky");
            Mocker.GetMock<IMangaService>()
                  .Verify(s => s.UpdateManga(_manga, false), Times.Once);
        }

        // ===================== Suppression semantic =====================

        [Test]
        public void UpdateManga_is_always_called_with_publishUpdatedEvent_false()
        {
            // Phase 10-07 2-arg overload -- D-06 rationale -- suppress event flood.
            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(_manga))
                  .Returns(new AutoTaggingChanges { TagsToAdd = new HashSet<int> { 5 } });

            Subject.Handle(new MangaAddedEvent(_manga));

            // Must NEVER call the 1-arg or default-true overload.
            Mocker.GetMock<IMangaService>()
                  .Verify(s => s.UpdateManga(It.IsAny<NzbDrone.Core.Manga.Manga>(), true),
                          Times.Never);
            Mocker.GetMock<IMangaService>()
                  .Verify(s => s.UpdateManga(It.IsAny<NzbDrone.Core.Manga.Manga>(), false),
                          Times.Once);
        }
    }
}
