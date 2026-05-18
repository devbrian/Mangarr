using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.Validation
{
    // Phase 24 Plan 24-04 — AutoTagging Slot 8 flip coverage. Mirrors the sibling
    // TagInUseValidatorFixture (Phase 22 Plan 22-04) shape; covers the cases that
    // pin the validator's behavior now that
    // _autoTaggingService.AllForTag(tag.Id).Count is live (not stubbed-false).
    //
    // 5-case surface:
    //   1. valid_when_no_autotagging_rules_reference_tag (0-rule path — Validate
    //      returns IsValid=true when AutoTagging is the only potential consumer
    //      and the rule list is empty)
    //   2. invalid_with_one_autotagging_rule (1-rule singular form "1 auto-tagging rule")
    //   3. invalid_with_three_autotagging_rules (3-rule plural form "3 auto-tagging rules")
    //   4. totalCount_aggregates_with_sibling_consumers (Manga + AutoTagging both
    //      hold the tag — error mentions both, total = 1 + 1 = 2)
    //   5. validator_calls_AllForTag_with_tag_id (mock-Verify the method was invoked
    //      with the correct tag id — defense-in-depth against accidental
    //      hard-coded id or omitted call)
    [TestFixture]
    public class TagInUseValidatorAutoTaggingFixture : CoreTest<TagInUseValidator>
    {
        private Tag _tag = null!;

        [SetUp]
        public void Setup()
        {
            _tag = new Tag { Id = 1, Label = "fantasy" };
        }

        private void SetupDetails(TagDetails details)
        {
            Mocker.GetMock<ITagService>()
                  .Setup(s => s.Details(_tag.Id))
                  .Returns(details);
        }

        private void SetupAutoTagging(int count)
        {
            // Build `count` AutoTag rules referencing _tag.Id.
            var rules = new List<AutoTag>();
            for (var i = 0; i < count; i++)
            {
                rules.Add(new AutoTag
                {
                    Id = 100 + i,
                    Name = $"rule-{i}",
                    Tags = new HashSet<int> { _tag.Id }
                });
            }

            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.AllForTag(_tag.Id))
                  .Returns(rules);
        }

        [Test]
        public void valid_when_no_autotagging_rules_reference_tag()
        {
            SetupDetails(new TagDetails
            {
                Id = _tag.Id,
                Label = _tag.Label,
                MangaIds = new List<int>(),
                DelayProfileIds = new List<int>(),
                NotificationIds = new List<int>(),
                RestrictionIds = new List<int>(),
                ExcludedReleaseProfileIds = new List<int>(),
                IndexerIds = new List<int>(),
                DownloadClientIds = new List<int>()
            });

            SetupAutoTagging(0);

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void invalid_with_one_autotagging_rule()
        {
            SetupDetails(new TagDetails
            {
                Id = _tag.Id,
                Label = _tag.Label,
                MangaIds = new List<int>(),
                DelayProfileIds = new List<int>(),
                NotificationIds = new List<int>(),
                RestrictionIds = new List<int>(),
                ExcludedReleaseProfileIds = new List<int>(),
                IndexerIds = new List<int>(),
                DownloadClientIds = new List<int>()
            });

            SetupAutoTagging(1);

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle();
            result.Errors[0].ErrorMessage.Should().Contain("1 auto-tagging rule");
            result.Errors[0].ErrorMessage.Should().NotContain("auto-tagging rules");
        }

        [Test]
        public void invalid_with_three_autotagging_rules()
        {
            SetupDetails(new TagDetails
            {
                Id = _tag.Id,
                Label = _tag.Label,
                MangaIds = new List<int>(),
                DelayProfileIds = new List<int>(),
                NotificationIds = new List<int>(),
                RestrictionIds = new List<int>(),
                ExcludedReleaseProfileIds = new List<int>(),
                IndexerIds = new List<int>(),
                DownloadClientIds = new List<int>()
            });

            SetupAutoTagging(3);

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle();
            result.Errors[0].ErrorMessage.Should().Contain("3 auto-tagging rules");
        }

        [Test]
        public void totalCount_aggregates_with_sibling_consumers()
        {
            // 1 manga + 1 autotagging rule => total = 2 items; error mentions both.
            SetupDetails(new TagDetails
            {
                Id = _tag.Id,
                Label = _tag.Label,
                MangaIds = new List<int> { 42 }
            });

            SetupAutoTagging(1);

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle();
            var msg = result.Errors[0].ErrorMessage;
            msg.Should().Contain("1 manga");
            msg.Should().Contain("1 auto-tagging rule");
            msg.Should().Contain("by 2 items"); // totalCount = 1 + 1 = 2
        }

        [Test]
        public void validator_calls_AllForTag_with_tag_id()
        {
            SetupDetails(new TagDetails
            {
                Id = _tag.Id,
                Label = _tag.Label,
                MangaIds = new List<int>(),
                DelayProfileIds = new List<int>(),
                NotificationIds = new List<int>(),
                RestrictionIds = new List<int>(),
                ExcludedReleaseProfileIds = new List<int>(),
                IndexerIds = new List<int>(),
                DownloadClientIds = new List<int>()
            });

            SetupAutoTagging(0);

            Subject.Validate(_tag);

            Mocker.GetMock<IAutoTaggingService>()
                  .Verify(s => s.AllForTag(_tag.Id), Times.Once);
        }
    }
}
