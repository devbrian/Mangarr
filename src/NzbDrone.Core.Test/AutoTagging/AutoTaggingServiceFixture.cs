using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging
{
    // Phase 24 v1.1 Wave-0 — verifies AutoTaggingService restored from 6f857ba0e^
    // (Pitfall 3 anti-rewrite gate context) — GetTagChanges algorithm with
    // RemoveTagsAutomatically opt-in path (D-05 Sonarr-canonical), plus the
    // Insert/Update/Delete → AutoTagsUpdatedEvent publish contract.
    //
    // Note: AutoTaggingService ctor takes IRootFolderService (NOT the concrete
    // RootFolderService from Sonarr's 6f857ba0e^ verbatim source — adapted to
    // Mangarr's interface-injection DI convention so AutoMoq can satisfy the
    // RootFolder dep cleanly. Documented as Rule-3 deviation in 24-02-SUMMARY.md;
    // structural behavior identical because IRootFolderService.GetBestRootFolderPath
    // is the same method.).
    [TestFixture]
    public class AutoTaggingServiceFixture : CoreTest<AutoTaggingService>
    {
        private NzbDrone.Core.Manga.Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = new NzbDrone.Core.Manga.Manga
            {
                Id = 1,
                Title = "Test Manga",
                Path = "C:/manga/test",
                Tags = new HashSet<int>(),
            };

            // Real ICacheManager — the service uses cacheManager.GetCache<>() at ctor and
            // _cache.Clear() on Insert/Update/Delete; mocking Cache<T> requires interface
            // setup that is heavier than just using the real implementation.
            Mocker.SetConstant<ICacheManager>(new CacheManager());

            // Default to empty repository.
            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<AutoTag>());

            // Root folder service no-op — most tests won't iterate manga; for tests that do,
            // override per-test.
            Mocker.GetMock<IRootFolderService>()
                  .Setup(r => r.GetBestRootFolderPath(It.IsAny<string>()))
                  .Returns<string>(p => p);
        }

        // ===================== GetTagChanges algorithm =====================

        [Test]
        public void GetTagChanges_with_no_rules_returns_empty_changes()
        {
            var changes = Subject.GetTagChanges(_manga);

            changes.TagsToAdd.Should().BeEmpty();
            changes.TagsToRemove.Should().BeEmpty();
        }

        [Test]
        public void GetTagChanges_with_all_specs_matching_adds_rule_tags_not_already_on_manga()
        {
            var matchingSpec = BuildSpec(matches: true, required: false);

            var autoTag = new AutoTag
            {
                Id = 100,
                Name = "Test Rule",
                Specifications = new List<IAutoTaggingSpecification> { matchingSpec.Object },
                Tags = new HashSet<int> { 7 },
            };

            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<AutoTag> { autoTag });

            var changes = Subject.GetTagChanges(_manga);

            changes.TagsToAdd.Should().BeEquivalentTo(new[] { 7 });
            changes.TagsToRemove.Should().BeEmpty();
        }

        [Test]
        public void GetTagChanges_with_all_specs_matching_skips_tags_already_on_manga()
        {
            _manga.Tags.Add(7);

            var matchingSpec = BuildSpec(matches: true, required: false);

            var autoTag = new AutoTag
            {
                Id = 100,
                Name = "Test Rule",
                Specifications = new List<IAutoTaggingSpecification> { matchingSpec.Object },
                Tags = new HashSet<int> { 7 },
            };

            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<AutoTag> { autoTag });

            var changes = Subject.GetTagChanges(_manga);

            changes.TagsToAdd.Should().BeEmpty();
            changes.TagsToRemove.Should().BeEmpty();
        }

        [Test]
        public void GetTagChanges_with_no_match_and_RemoveTagsAutomatically_false_does_not_remove_tags()
        {
            // D-05: user-applied-tags-sticky default. Rule with RemoveTagsAutomatically=false
            // (Sonarr-canonical default) does NOT clear its tags when it stops matching.
            var failingSpec = BuildSpec(matches: false, required: false);

            var autoTag = new AutoTag
            {
                Id = 100,
                Name = "Test Rule",
                Specifications = new List<IAutoTaggingSpecification> { failingSpec.Object },
                RemoveTagsAutomatically = false,
                Tags = new HashSet<int> { 7 },
            };

            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<AutoTag> { autoTag });

            var changes = Subject.GetTagChanges(_manga);

            changes.TagsToAdd.Should().BeEmpty();
            changes.TagsToRemove.Should().BeEmpty("RemoveTagsAutomatically=false (D-05 default) — user-applied tags stick");
        }

        [Test]
        public void GetTagChanges_with_no_match_and_RemoveTagsAutomatically_true_removes_rule_tags()
        {
            var failingSpec = BuildSpec(matches: false, required: false);

            var autoTag = new AutoTag
            {
                Id = 100,
                Name = "Test Rule",
                Specifications = new List<IAutoTaggingSpecification> { failingSpec.Object },
                RemoveTagsAutomatically = true,
                Tags = new HashSet<int> { 7 },
            };

            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<AutoTag> { autoTag });

            var changes = Subject.GetTagChanges(_manga);

            changes.TagsToAdd.Should().BeEmpty();
            changes.TagsToRemove.Should().BeEquivalentTo(new[] { 7 }, "Sonarr-canonical opt-in: RemoveTagsAutomatically=true sweeps rule tags");
        }

        // ===================== Insert / Update / Delete event-publish contract =====================

        [Test]
        public void Insert_publishes_AutoTagsUpdatedEvent()
        {
            var autoTag = new AutoTag { Id = 0, Name = "New Rule" };
            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.Insert(It.IsAny<AutoTag>()))
                  .Returns<AutoTag>(t =>
                  {
                      t.Id = 1;
                      return t;
                  });

            Subject.Insert(autoTag);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<AutoTagsUpdatedEvent>()), Times.Once);
        }

        [Test]
        public void Update_publishes_AutoTagsUpdatedEvent()
        {
            var autoTag = new AutoTag { Id = 1, Name = "Existing Rule" };

            Subject.Update(autoTag);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<AutoTagsUpdatedEvent>()), Times.Once);
        }

        [Test]
        public void Delete_publishes_AutoTagsUpdatedEvent()
        {
            Subject.Delete(1);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<AutoTagsUpdatedEvent>()), Times.Once);
        }

        // ===================== AllForTag =====================

        [Test]
        public void AllForTag_returns_rules_whose_Tags_hashset_contains_the_tagId()
        {
            var t1 = new AutoTag { Id = 1, Name = "Rule 1", Tags = new HashSet<int> { 7 } };
            var t2 = new AutoTag { Id = 2, Name = "Rule 2", Tags = new HashSet<int> { 8 } };
            var t3 = new AutoTag { Id = 3, Name = "Rule 3", Tags = new HashSet<int> { 7, 8 } };

            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.All())
                  .Returns(new List<AutoTag> { t1, t2, t3 });

            var matches = Subject.AllForTag(7);

            matches.Should().HaveCount(2);
            matches.Should().Contain(r => r.Id == 1);
            matches.Should().Contain(r => r.Id == 3);
        }

        // ===================== helpers =====================

        private static Mock<IAutoTaggingSpecification> BuildSpec(bool matches, bool required)
        {
            var spec = new Mock<IAutoTaggingSpecification>();
            spec.Setup(s => s.IsSatisfiedBy(It.IsAny<NzbDrone.Core.Manga.Manga>())).Returns(matches);
            spec.SetupGet(s => s.Required).Returns(required);
            return spec;
        }
    }
}
