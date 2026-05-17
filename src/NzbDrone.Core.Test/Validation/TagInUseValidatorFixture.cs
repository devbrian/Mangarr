using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.Validation
{
    // Phase 22 Plan 22-04 — RED-before-GREEN unit gate for TagInUseValidator.
    //
    // CoreTest<TagInUseValidator> harness pattern lifted from
    // src/NzbDrone.Core.Test/Manga/AddMangaServiceFixture.cs:32-100
    // (closest in-repo analog — Mocker.GetMock<...>().Setup pattern for
    // FluentValidation.Results.ValidationResult consumers).
    //
    // 7-test surface (per PATTERNS.md §TagInUseValidatorFixture.cs §Tests to ship):
    //   1. valid_when_no_consumer_holds_tag
    //   2. invalid_when_manga_holds_tag (+ named-count assertion "2 manga")
    //   3. invalid_when_notification_holds_tag (+ named-count assertion "1 connection" — D-06 frontend label)
    //   4. invalid_when_multiple_consumers_hold_tag (+ joined-consumer-summary)
    //   5. ignores_importlist_stub_false_branch (D-03 invariant pin — Phase 26 flip-test will tighten)
    //   6. ignores_autotagging_stub_false_branch (D-03 invariant pin — Phase 24 flip-test will tighten)
    //   7. error_message_does_not_leak_consumer_ids (V8 errors & logging compliance — counts only)
    [TestFixture]
    public class TagInUseValidatorFixture : CoreTest<TagInUseValidator>
    {
        private Tag _tag;

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

        [Test]
        public void valid_when_no_consumer_holds_tag()
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

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void invalid_when_manga_holds_tag()
        {
            SetupDetails(new TagDetails
            {
                Id = _tag.Id,
                Label = _tag.Label,
                MangaIds = new List<int> { 42, 67 }
            });

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle();
            result.Errors[0].ErrorMessage.Should().Contain("2 manga");
        }

        [Test]
        public void invalid_when_notification_holds_tag()
        {
            SetupDetails(new TagDetails
            {
                Id = _tag.Id,
                Label = _tag.Label,
                NotificationIds = new List<int> { 1 }
            });

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle();
            result.Errors[0].ErrorMessage.Should().Contain("1 connection");
        }

        [Test]
        public void invalid_when_multiple_consumers_hold_tag()
        {
            SetupDetails(new TagDetails
            {
                Id = _tag.Id,
                Label = _tag.Label,
                MangaIds = new List<int> { 42, 67 },
                NotificationIds = new List<int> { 1 }
            });

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle();
            var msg = result.Errors[0].ErrorMessage;
            msg.Should().Contain("2 manga");
            msg.Should().Contain("1 connection");
        }

        [Test]
        public void ignores_importlist_stub_false_branch()
        {
            // D-03 stub-false invariant pin: TagDetails has no ImportListIds slot
            // (ImportList consumer is stubbed false until Phase 26 flips it).
            // Validator must not NRE / false-positive on the missing slot.
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

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void ignores_autotagging_stub_false_branch()
        {
            // D-03 stub-false invariant pin: TagDetails has no AutoTaggingIds slot
            // (AutoTagging consumer is stubbed false until Phase 24 flips it).
            // Validator must not NRE / false-positive on the missing slot.
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

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void error_message_does_not_leak_consumer_ids()
        {
            // V8 errors & logging compliance: counts only, never row IDs.
            SetupDetails(new TagDetails
            {
                Id = _tag.Id,
                Label = _tag.Label,
                MangaIds = new List<int> { 42, 67 }
            });

            var result = Subject.Validate(_tag);

            result.IsValid.Should().BeFalse();
            result.Errors[0].ErrorMessage.Should().NotContain("42").And.NotContain("67");
        }
    }
}
